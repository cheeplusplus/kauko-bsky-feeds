using System.Net.WebSockets;
using System.Text.Json;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using Microsoft.Extensions.Logging;
using ZstdSharp;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public class JetstreamConsumerNativeWs : BaseJetstreamConsumer
{
    private readonly ILogger<JetstreamConsumerNativeWs> _logger;
    private readonly Decompressor _decompressor = GetDecompressor();
    private ClientWebSocket? _wsClient;
    private readonly CancellationTokenSource _cancelSource;

    public JetstreamConsumerNativeWs(ILogger<JetstreamConsumerNativeWs> logger)
    {
        _logger = logger;
        _cancelSource = new CancellationTokenSource();
    }

    public override async Task Start(
        Func<long?>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    )
    {
        var wsUri = GetWsUri(getCursor: getCursor, wantedCollections: wantedCollections);

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
                        _logger.LogError(ex, "Got exception in listen loop");
                    }
                } while (!_cancelSource.IsCancellationRequested);

                _logger.LogWarning("Consumer is shutting down");

                await _wsClient.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    null,
                    cancellationToken
                );
            },
            cancellationToken
        );
    }

    public override Task Stop()
    {
        _cancelSource.Cancel();
        return Task.CompletedTask;
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
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Zstd compressed
                    using var dc = new DecompressionStream(msgStream, _decompressor);
                    try
                    {
                        await dc.CopyToAsync(textStream, cancellationToken);
                    }
                    catch (ZstdException zex)
                    {
                        _logger.LogError(zex, "Decompression error");
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
                    OnMessage(deserialized);
                }
            }
            catch (JsonException jex)
            {
                _logger.LogError(jex, "JSON deserialization error");

                try
                {
                    // Reread the stream to see if we can figure out why this is happening
                    textStream.Seek(0, SeekOrigin.Begin);
                    using var sr = new StreamReader(textStream);
                    var lines = await sr.ReadToEndAsync(cancellationToken);
                    _logger.LogInformation("JSON failure text was: {lines}", lines);
                }
                catch (Exception zexi)
                {
                    _logger.LogError(zexi, "Got error trying to decode the decompression failure");
                }
                throw;
            }
        }

        // TODO: Reconnect instead of killing everything
        await _cancelSource.CancelAsync();
    }
}
