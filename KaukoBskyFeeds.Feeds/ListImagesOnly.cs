using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(ListImagesOnly), typeof(ListImagesOnlyFeedConfig))]
public class ListImagesOnly(
    ATProtocol proto,
    ListImagesOnlyFeedConfig feedConfig,
    FeedDbContext db,
    IBskyCache cache
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
        var listMemberDids = await cache.GetListMembers(
            proto,
            new ATUri(feedConfig.ListUri),
            cancellationToken
        );
        var feedDids = listMemberDids.Select(s => s.Handler);

        var posts = await db
            .Posts.LatestFromCursor(cursor)
            .Where(w =>
                feedDids.Contains(w.Did)
                && w.ImageCount > 0
                && (w.ReplyParentUri == null || w.ReplyParentUri.StartsWith("at://" + w.Did))
            )
            .Take(limit ?? 50)
            .ToListAsync(cancellationToken);
        var filteredFeed = posts.Select(s => new CustomSkeletonFeedPost(s.ToUri()));
        var newCursor = posts.LastOrDefault()?.GetCursor();

        return new CustomSkeletonFeed(filteredFeed.ToList(), newCursor);
    }
}
