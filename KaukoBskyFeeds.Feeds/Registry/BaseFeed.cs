using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Feeds.Registry;

public abstract class BaseFeed(ATUri feedUri, BaseFeedConfig baseConfig) : IFeed
{
    public abstract Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    );

    public BaseFeedConfig Config => baseConfig;

    protected string GetStandardFeedContext(string? feedContext)
    {
        var ctx = new StandardFeedContext
        {
            FeedUri = feedUri.ToString(),
            ProvidedCtx = feedContext,
        };

        return ctx.Serialize();
    }

    protected string GetDbCacheName(ATDid? requestor = null, string? cursor = null)
    {
        return $"feed_db:{{{feedUri.AsSafeKey()}}}:{requestor?.AsSafeKey() ?? ""}:{cursor ?? ""}";
    }

    protected string[] DbCacheTags => ["feed", "feed/db", $"feed/{feedUri}"];
}
