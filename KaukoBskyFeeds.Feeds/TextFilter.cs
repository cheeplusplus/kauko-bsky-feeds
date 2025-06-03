using System.Linq.Expressions;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Post = KaukoBskyFeeds.Db.Models.Post;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(TextFilter), typeof(TextFilterFeedConfig))]
public class TextFilter(
    ILogger<TimelineMinusList> logger,
    ATProtocol proto,
    TextFilterFeedConfig feedConfig,
    FeedDbContext db,
    BskyMetrics bskyMetrics,
    HybridCache mCache,
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
        if (proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        List<string> filterToDids = [];
        if (feedConfig.FeedSource == FeedSourceSetting.Following)
        {
            var followingList = (
                await bsCache.GetFollowing(proto, proto.Session.Did, cancellationToken)
            );
            filterToDids = followingList.Select(s => s.Handler).ToList();
        }
        else if (feedConfig is { FeedSource: FeedSourceSetting.List, FeedSourceList: not null })
        {
            var listMembersList = (
                await bsCache.GetListMembers(proto, ATUri.Create(feedConfig.FeedSourceList), cancellationToken)
            );
            filterToDids = listMembersList.Select(s => s.Handler).ToList();
        }

        var posts = await mCache.GetOrCreateAsync(
            $"feed_db_{feedMeta.FeedUri}|{cursor}",
            async (_) =>
            {
                var q = db.Posts.LatestFromCursor(cursor);
                if (filterToDids.Count > 0)
                {
                    q = q.Where(w => filterToDids.Contains(w.Did));
                }

                foreach (var op in feedConfig.FilterOperations.Values.SelectMany(s => s))
                {
                    if (op.Exclude)
                    {
                        if (op.Contains != null)
                        {
                            q = q.Where(w => !w.Text.Contains(op.Contains));
                        }

                        if (op.Expression != null)
                        {
                            q = q.Where(w => !op.Expression.IsMatch(w.Text));
                        }
                    }
                    else
                    {
                        if (op.Contains != null)
                        {
                            q = q.Where(w => w.Text.Contains(op.Contains));
                        }

                        if (op.Expression != null)
                        {
                            q = q.Where(w => op.Expression.IsMatch(w.Text));
                        }
                    }
                }

                return await q.Take(limit ?? 50)
                    .ToListAsync(cancellationToken);
            },
            BskyCache.QuickOpts,
            tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
            cancellationToken: cancellationToken
        );

        if (posts.Count < 1 || cancellationToken.IsCancellationRequested)
        {
            return new CustomSkeletonFeed([], null);
        }

        var feedOutput =
            posts.Select(s => new SkeletonFeedPost(s.ToAtUri()));
        return new CustomSkeletonFeed(feedOutput, posts.LastOrDefault()?.CreatedAt.AsCursor());
    }
}