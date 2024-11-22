using System.Reflection;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Shared.Bsky;
using ZstdSharp;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public interface IJetstreamConsumer
{
    event EventHandler<JetstreamMessage>? Message;
    long LastEventTime { get; }
    Task Start(
        Func<long?>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    );
    Task Stop();
}

public abstract class BaseJetstreamConsumer : IJetstreamConsumer
{
    public const string JETSTREAM_URL = "wss://jetstream1.us-west.bsky.network";
    public static readonly string[] DEFAULT_COLLECTIONS = [BskyConstants.COLLECTION_TYPE_POST];

    public event EventHandler<JetstreamMessage>? Message;
    public long LastEventTime { get; protected set; }
    public abstract Task Start(
        Func<long?>? getCursor = null,
        IEnumerable<string>? wantedCollections = null,
        CancellationToken cancellationToken = default
    );
    public abstract Task Stop();

    protected virtual void OnMessage(JetstreamMessage message)
    {
        Message?.Invoke(this, message);
    }

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

    protected static Uri GetWsUri(
        string hostUrl = JETSTREAM_URL,
        Func<long?>? getCursor = null,
        IEnumerable<string>? wantedCollections = null
    )
    {
        long? cursor = getCursor == null ? null : getCursor();

        // require an actually empty list to send nothing
        wantedCollections ??= DEFAULT_COLLECTIONS;

        var querySegments = new Dictionary<string, string>();
        if (cursor != null && cursor >= 0)
        {
            var cursorStr = cursor.ToString();
            if (cursorStr != null)
            {
                querySegments.Add("cursor", cursorStr);
            }
        }
        if (wantedCollections?.Any() ?? false)
        {
            querySegments.Add("wantedCollections", string.Join(',', wantedCollections));
        }
        var queryStr =
            querySegments.Count > 0
                ? "?" + string.Join('&', querySegments.Select(s => $"{s.Key}={s.Value}"))
                : null;

        var jetstreamUri = new UriBuilder(hostUrl) { Path = "/subscribe", Query = queryStr };

        return jetstreamUri.Uri;
    }
}
