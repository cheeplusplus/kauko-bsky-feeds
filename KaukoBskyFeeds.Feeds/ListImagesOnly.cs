using System.Linq.Expressions;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(ListImagesOnly), typeof(ListImagesOnlyFeedConfig))]
public class ListImagesOnly(
    ATProtocol proto,
    ListImagesOnlyFeedConfig feedConfig,
    FeedDbContext db,
    BskyMetrics bskyMetrics,
    IMemoryCache mCache,
    IBskyCache bsCache,
    FeedInstanceMetadata feedMeta
) : IFeed
{
    public BaseFeedConfig Config => feedConfig;

    public async Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        var listUri = new ATUri(feedConfig.ListUri);
        var listMemberDids = await bsCache.GetListMembers(proto, listUri, cancellationToken);
        var feedDids = listMemberDids.Select(s => s.Handler);

        Expression<Func<Db.Models.Post, bool>> filter = w =>
            feedDids.Contains(w.Did)
            && w.ImageCount > 0
            && (w.ReplyParentUri == null || w.ReplyParentUri.StartsWith("at://" + w.Did));

        List<Db.Models.Post> posts;
        string? newCursor = null;
        if (feedConfig.FetchTimeline)
        {
            var postTlRes = await proto
                .Feed.GetListFeedAsync(listUri, limit ?? 50, cancellationToken)
                .Record(bskyMetrics, "app.bsky.feed.getListFeed");
            var postTl = postTlRes.HandleResult();
            posts = postTl
                .Feed.Where(w => w.Reason == null)
                .Select(s => s.ToDbPost())
                .AsQueryable()
                .Where(filter)
                .ToList();
            newCursor = postTl?.Cursor;
        }
        else
        {
            posts =
                await mCache.GetOrCreateAsync(
                    $"feed_db_{feedMeta.FeedUri}|{cursor}",
                    async (_) =>
                    {
                        return await db
                            .Posts.LatestFromCursor(cursor)
                            .Where(filter)
                            // Always take the same amount, postgres is being weird about response time with lower limits
                            .Take(50)
                            .ToListAsync(cancellationToken);
                    },
                    BskyCache.QUICK_OPTS
                ) ?? [];
            if (limit.HasValue)
            {
                // Limit artificially to keep the database from being weirdly slow
                posts = posts[..limit.Value];
            }
            newCursor = posts.LastOrDefault()?.GetCursor();
        }

        var filteredFeed = posts.Select(s => new CustomSkeletonFeedPost(s.ToUri()));

        return new CustomSkeletonFeed(filteredFeed.ToList(), newCursor);
    }
}
