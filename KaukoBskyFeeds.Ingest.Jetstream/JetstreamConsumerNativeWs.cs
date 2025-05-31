using System.Net.WebSockets;
using System.Text.Json;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.Extensions.Logging;
using ZstdSharp;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public class JetstreamConsumerNativeWs(
    ILogger<JetstreamConsumerNativeWs> logger,
    JetstreamMetrics metrics
) : BaseJetstreamConsumer
{
    private readonly Decompressor _decompressor = GetDecompressor();
    private ClientWebSocket? _wsClient;
    private readonly CancellationTokenSource _cancelSource = new();

    public override async Task Start(
        Func<CancellationToken, Task<long?>>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    )
    {
        var wsUri = await GetWsUri(
            getCursor: getCursor,
            wantedCollections: wantedCollections,
            cancellationToken: cancellationToken
        );

        _wsClient = new ClientWebSocket();
        _wsClient.Options.SetRequestHeader("Socket-Encoding", "zstd");
        _wsClient.Options.SetRequestHeader("User-Agent", "KaukoBskyFeeds.Ingest.Jetstream/1.0");
        await _wsClient.ConnectAsync(wsUri, cancellationToken);

        _ = Task.Run(
            async () =>
            {
                // TODO: Maybe add a retry backoff/limit
                // It's possible to crash in here but we also may need to handle reconnecting
                do
                {
                    try
                    {
                        await Listen(_cancelSource.Token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Got exception in listen loop");
                        metrics.WsError(wsUri.Host, ex.GetType().Name);
                    }

                    metrics.WsReconnect(wsUri.Host);
                } while (!_cancelSource.IsCancellationRequested);

                logger.LogWarning("Consumer is shutting down");

                await _wsClient.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    null,
                    cancellationToken
                );
            },
            cancellationToken
        );
    }

    public override async Task Stop()
    {
        await base.Stop();
        await _cancelSource.CancelAsync();
    }

    private async Task Listen(CancellationToken cancellationToken)
    {
        if (_wsClient == null)
        {
            return;
        }

        var buffer = WebSocket.CreateClientBuffer(1024, 1024);

        while (_wsClient.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;

            using var textStream = new MemoryStream();
            using (var msgStream = new MemoryStream())
            {
                do
                {
                    result = await _wsClient.ReceiveAsync(buffer, cancellationToken);
                    msgStream.Write(buffer.Array!, 0, result.Count);
                } while (!result.EndOfMessage);
                msgStream.Seek(0, SeekOrigin.Begin);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await msgStream.CopyToAsync(textStream, cancellationToken);
                    metrics.SawEvent(textStream.Length);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Zstd compressed
                    using var dc = new DecompressionStream(msgStream, _decompressor);
                    try
                    {
                        await dc.CopyToAsync(textStream, cancellationToken);
                        metrics.SawEvent(textStream.Length, msgStream.Length);
                    }
                    catch (ZstdException zex)
                    {
                        logger.LogError(zex, "Decompression error");
                        metrics.SawEventGenericError(zex.GetType().Name);
                        throw;
                    }
                }
                else
                {
                    continue;
                }
            }

            // Deserialize
            textStream.Seek(0, SeekOrigin.Begin);
            try
            {
                var deserialized = await JsonSerializer.DeserializeAsync<JetstreamMessage>(
                    textStream,
                    cancellationToken: cancellationToken
                );
                if (deserialized != null)
                {
                    LastEventTime = deserialized.TimeMicroseconds;
                    await ChannelWriter.WriteAsync(deserialized, cancellationToken);
                    metrics.SawEventParsed(deserialized.Commit?.Collection ?? "_unknown_");
                }
            }
            catch (JsonException jex)
            {
                logger.LogError(jex, "JSON deserialization error");
                metrics.SawEventParseError(jex.GetType().Name);

                try
                {
                    // Reread the stream to see if we can figure out why this is happening
                    textStream.Seek(0, SeekOrigin.Begin);
                    using var sr = new StreamReader(textStream);
                    var lines = await sr.ReadToEndAsync(cancellationToken);
                    logger.LogInformation("JSON failure text was: {lines}", lines);
                }
                catch (Exception zexi)
                {
                    logger.LogError(zexi, "Got error trying to decode the decompression failure");
                }
                throw;
            }
        }

        // TODO: Reconnect instead of killing everything
        await _cancelSource.CancelAsync();
    }
}
