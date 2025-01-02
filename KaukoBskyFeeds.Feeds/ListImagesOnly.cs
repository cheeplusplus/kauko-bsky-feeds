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
        var listUri = new ATUri(feedConfig.ListUri);
        var listMemberDids = await cache.GetListMembers(proto, listUri, cancellationToken);
        var feedDids = listMemberDids.Select(s => s.Handler);

        Expression<Func<Db.Models.Post, bool>> filter = (Db.Models.Post w) =>
            feedDids.Contains(w.Did)
            && w.ImageCount > 0
            && (w.ReplyParentUri == null || w.ReplyParentUri.StartsWith("at://" + w.Did));

        List<Db.Models.Post> posts;
        string? newCursor = null;
        if (feedConfig.FetchTimeline)
        {
            var postTlRes = await proto.Feed.GetListFeedAsync(listUri, 50, cancellationToken);
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
            posts = await db
                .Posts.LatestFromCursor(cursor)
                .Where(filter)
                .Take(limit ?? 50)
                .ToListAsync(cancellationToken);
            newCursor = posts.LastOrDefault()?.GetCursor();
        }

        var filteredFeed = posts.Select(s => new CustomSkeletonFeedPost(s.ToUri()));

        return new CustomSkeletonFeed(filteredFeed.ToList(), newCursor);
    }
}
