using System.Net.WebSockets;
using System.Text.Json;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using Microsoft.Extensions.Logging;
using Websocket.Client;
using ZstdSharp;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public class JetstreamConsumerWSC : BaseJetstreamConsumer
{
    private readonly ILogger<JetstreamConsumerWSC> _logger;
    private readonly Decompressor _decompressor = GetDecompressor();
    private WebsocketClient? _wsClient;
    private readonly CancellationTokenSource _cancelSource;

    public JetstreamConsumerWSC(ILogger<JetstreamConsumerWSC> logger)
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
        _wsClient.ReconnectionHappened.Subscribe(info =>
            _logger.LogInformation("Reconnection happened, type: {type}", info.Type)
        );
        _wsClient.DisconnectionHappened.Subscribe(info =>
        {
            _logger.LogInformation(
                "Disconnection happened, type: {type}, status: {closeStatus}, description: {closeDescription}",
                info.Type,
                info.CloseStatus,
                info.CloseStatusDescription
            );

            // Update the cursor URI so we restart at the right point
            _wsClient.Url = GetWsUri(getCursor: getCursor, wantedCollections: wantedCollections);
        });
        _wsClient.MessageReceived.Subscribe(OnMessageReceived);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await _wsClient.Start();
    }

    public override async Task Stop()
    {
        if (_wsClient != null)
        {
            await _wsClient.Stop(WebSocketCloseStatus.NormalClosure, "");
        }
        _cancelSource.Cancel();
    }

    private void OnMessageReceived(ResponseMessage message)
    {
        if (_cancelSource.IsCancellationRequested)
        {
            return;
        }

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
            return;
        }

        var deserializedMsg = JsonSerializer.Deserialize<JetstreamMessage>(serializedStr);
        if (deserializedMsg != null)
        {
            LastEventTime = deserializedMsg.TimeMicroseconds;
            OnMessage(deserializedMsg);
        }
    }
}
