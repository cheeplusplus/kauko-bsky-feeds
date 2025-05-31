using System.Reflection;
using System.Threading.Channels;
using Castle.Core.Internal;
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
    public static readonly string[] JetstreamUrls =
    [
        "wss://jetstream1.us-east.bsky.network",
        "wss://jetstream2.us-east.bsky.network",
        "wss://jetstream1.us-west.bsky.network",
        "wss://jetstream2.us-west.bsky.network",
    ];
    public static readonly string[] DefaultCollections =
    [
        BskyConstants.CollectionTypePost,
        BskyConstants.CollectionTypeLike,
        BskyConstants.CollectionTypeRepost,
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
            ?? Random.Shared.GetItems(JetstreamUrls, 1).FirstOrDefault()
            ?? JetstreamUrls[0];
        long? cursor = getCursor == null ? null : await getCursor(cancellationToken);

        // require an actually empty list to send nothing
        var wantedCollectionsList = (wantedCollections ?? DefaultCollections).ToList();

        var querySegments = new List<KeyValuePair<string, string>>();
        if (cursor != null && cursor >= 0)
        {
            var replayCursor = cursor.Value - (REPLAY_WINDOW_MS * 1000); // cursor is in microseconds
            replayCursor = Math.Max(replayCursor, 0);

            var cursorStr = replayCursor.ToString();
            if (!cursorStr.IsNullOrEmpty())
            {
                querySegments.Add(new KeyValuePair<string, string>("cursor", cursorStr));
            }
        }
        if (wantedCollectionsList.Count > 0)
        {
            foreach (var coll in wantedCollectionsList)
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
