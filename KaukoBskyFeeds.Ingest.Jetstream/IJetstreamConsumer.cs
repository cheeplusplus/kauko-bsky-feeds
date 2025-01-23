using System.Reflection;
using System.Threading.Channels;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Shared.Bsky;
using ZstdSharp;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public interface IJetstreamConsumer
{
    public ChannelReader<JetstreamMessage> ChannelReader { get; }
    long LastEventTime { get; }
    Task Start(
        Func<CancellationToken, Task<long?>>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    );
    Task Stop();
}

public abstract class BaseJetstreamConsumer : IJetstreamConsumer
{
    public static readonly string[] JETSTREAM_URLS =
    [
        "wss://jetstream1.us-east.bsky.network",
        "wss://jetstream2.us-east.bsky.network",
        "wss://jetstream1.us-west.bsky.network",
        "wss://jetstream2.us-west.bsky.network",
    ];
    public static readonly string[] DEFAULT_COLLECTIONS =
    [
        BskyConstants.COLLECTION_TYPE_POST,
        BskyConstants.COLLECTION_TYPE_LIKE,
        BskyConstants.COLLECTION_TYPE_REPOST,
    ];
    private const int REPLAY_WINDOW_MS = 3000;

    private readonly Channel<JetstreamMessage> _channel = Channel.CreateBounded<JetstreamMessage>(
        10
    );

    public ChannelReader<JetstreamMessage> ChannelReader => _channel.Reader;
    public long LastEventTime { get; protected set; }
    public abstract Task Start(
        Func<CancellationToken, Task<long?>>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    );

    public virtual Task Stop()
    {
        _channel.Writer.TryComplete();
        return Task.CompletedTask;
    }

    protected ChannelWriter<JetstreamMessage> ChannelWriter => _channel.Writer;

    protected static Decompressor GetDecompressor()
    {
        var decompressor = new Decompressor();
        var dictResource =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("KaukoBskyFeeds.Ingest.Jetstream.data.zstd_dictionary")
            ?? throw new Exception("Failed to find zstd dictionary in assembly!");
        using var br = new BinaryReader(dictResource);
        var dict = br.ReadBytes((int)dictResource.Length);
        decompressor.LoadDictionary(dict);
        return decompressor;
    }

    protected static async Task<Uri> GetWsUri(
        Func<CancellationToken, Task<long?>>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        string? hostUrl = null,
        CancellationToken cancellationToken = default
    )
    {
        var chosenHostUrl =
            hostUrl
            ?? Random.Shared.GetItems(JETSTREAM_URLS, 1).FirstOrDefault()
            ?? JETSTREAM_URLS[0];
        long? cursor = getCursor == null ? null : await getCursor(cancellationToken);

        // require an actually empty list to send nothing
        wantedCollections ??= DEFAULT_COLLECTIONS;

        var querySegments = new List<KeyValuePair<string, string>>();
        if (cursor != null && cursor >= 0)
        {
            var replayCursor = cursor.Value - (REPLAY_WINDOW_MS * 1000); // cursor is in microseconds
            replayCursor = Math.Max(replayCursor, 0);

            var cursorStr = replayCursor.ToString();
            if (cursorStr != null)
            {
                querySegments.Add(new KeyValuePair<string, string>("cursor", cursorStr));
            }
        }
        if (wantedCollections?.Any() ?? false)
        {
            foreach (var coll in wantedCollections)
            {
                querySegments.Add(new KeyValuePair<string, string>("wantedCollections", coll));
            }
        }
        var queryStr =
            querySegments.Count > 0
                ? "?" + string.Join('&', querySegments.Select(s => $"{s.Key}={s.Value}"))
                : null;

        var jetstreamUri = new UriBuilder(chosenHostUrl) { Path = "/subscribe", Query = queryStr };

        return jetstreamUri.Uri;
    }
}
