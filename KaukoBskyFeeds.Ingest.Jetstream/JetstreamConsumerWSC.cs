using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Channels;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using Microsoft.Extensions.Logging;
using Websocket.Client;
using ZstdSharp;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public class JetstreamConsumerWSC(ILogger<JetstreamConsumerWSC> logger)
    : BaseJetstreamConsumer,
        IDisposable
{
    private readonly Decompressor _decompressor = GetDecompressor();
    private WebsocketClient? _wsClient;
    private readonly CancellationTokenSource _cancelSource = new();
    private readonly List<IDisposable> _disposers = [];

    public override async Task Start(
        Func<long?>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    )
    {
        var wsUri = GetWsUri(getCursor: getCursor, wantedCollections: wantedCollections);
        logger.LogInformation("Connecting to Jetstream with URI {uri}", wsUri);

        var factory = new Func<ClientWebSocket>(() =>
        {
            var client = new ClientWebSocket();
            client.Options.SetRequestHeader("Socket-Encoding", "zstd");
            client.Options.SetRequestHeader("User-Agent", "KaukoBskyFeeds.Ingest.Jetstream/1.0");
            return client;
        });

        _wsClient = new WebsocketClient(wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(30),
        };

        var reconnSubscription = _wsClient.ReconnectionHappened.Subscribe(info =>
        {
            if (info.Type == ReconnectionType.Initial)
            {
                logger.LogInformation("Initial connection");
            }
            else
            {
                logger.LogInformation("Reconnection happened, type: {type}", info.Type);
            }
        });
        _disposers.Add(reconnSubscription);

        var disconSubscription = _wsClient.DisconnectionHappened.Subscribe(info =>
        {
            if (info.Exception != null)
            {
                logger.LogError(info.Exception, "Got exception inside Websocket");
            }

            logger.LogInformation(
                "Disconnection happened, type: {type}, status: {closeStatus}, description: {closeDescription}",
                info.Type,
                info.CloseStatus,
                info.CloseStatusDescription
            );

            // Update the cursor URI so we restart at the right point, as long as we didn't intentionally disconnect
            if (
                info.Type != DisconnectionType.ByUser
                && info.CloseStatus != WebSocketCloseStatus.NormalClosure
            )
            {
                _wsClient.Url = GetWsUri(
                    getCursor: getCursor,
                    wantedCollections: wantedCollections
                );
                logger.LogDebug("Switching Jetstream URI to {uri}", _wsClient.Url);
            }
        });
        _disposers.Add(disconSubscription);

        var messageSubscription = _wsClient.MessageReceived.SubscribeAsync(OnMessageReceived);
        _disposers.Add(messageSubscription);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await _wsClient.StartOrFail();
    }

    public override async Task Stop()
    {
        await base.Stop();

        logger.LogDebug("Stopping consumer");
        if (_wsClient != null)
        {
            await _wsClient.Stop(WebSocketCloseStatus.NormalClosure, "");
        }
        _disposers.ForEach(f => f.Dispose());
        _cancelSource.Cancel();
    }

    private async Task OnMessageReceived(ResponseMessage message)
    {
        if (_cancelSource.IsCancellationRequested)
        {
            logger.LogDebug("OMR during cancellation");
            return;
        }

        try
        {
            string serializedStr;
            if (message.MessageType == WebSocketMessageType.Text)
            {
                serializedStr = message.Text!;
            }
            else if (message.MessageType == WebSocketMessageType.Binary)
            {
                using var ms = new MemoryStream(message.Binary!);
                using var ds = new DecompressionStream(ms, _decompressor);
                using var sr = new StreamReader(ds);
                serializedStr = sr.ReadToEnd();
            }
            else
            {
                logger.LogError("Got unknown message type: {msgType}", message.MessageType);
                return;
            }

            try
            {
                var deserializedMsg = JsonSerializer.Deserialize<JetstreamMessage>(serializedStr);
                if (deserializedMsg == null)
                {
                    logger.LogWarning("Null JSON: {json}", serializedStr);
                    return;
                }
                LastEventTime = deserializedMsg.TimeMicroseconds;
                await ChannelWriter.WriteAsync(deserializedMsg);
            }
            catch (JsonException je)
            {
                logger.LogError(je, "Failed to deserialize JSON: {json}", serializedStr);
            }
        }
        catch (ChannelClosedException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Got exception in message processing");
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _cancelSource.Cancel();
        _wsClient?.Dispose();
        _disposers.ForEach(f => f.Dispose());
    }
}

public static class RxExtensions
{
    public static IDisposable SubscribeAsync<T>(
        this IObservable<T> source,
        Func<T, Task> onNextAsync
    ) => source.Select(v => Observable.FromAsync(() => onNextAsync(v))).Concat().Subscribe();
}
