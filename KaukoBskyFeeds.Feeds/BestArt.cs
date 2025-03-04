using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(BestArt), typeof(BestArtFeedConfig))]
public class BestArt(
    ATProtocol proto,
    BestArtFeedConfig feedConfig,
    FeedDbContext db,
    IMemoryCache mCache,
    IBskyCache bsCache
) : IFeed
{
    private int FEED_LIMIT => feedConfig.FeedLimit;
    private int MIN_INTERACTIONS => feedConfig.MinInteractions;

    public BaseFeedConfig Config => feedConfig;

    public async Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        var result =
            await mCache.GetOrCreateAsync(
                $"BestArtFeed|list_{feedConfig.RestrictToListUri ?? "all"}|bal_{feedConfig.BalanceInteractions ?? false}",
                async (opts) =>
                {
                    return await GetFeedSkeletonUncached(cancellationToken);
                },
                BskyCache.DEFAULT_OPTS
            ) ?? [];

        // Limit artificially so we can reuse the cache
        if (limit.HasValue)
        {
            result = result.Take(limit.Value);
        }
        return new CustomSkeletonFeed(result, null);
    }

    private async Task<IEnumerable<CustomSkeletonFeedPost>> GetFeedSkeletonUncached(
        CancellationToken cancellationToken = default
    )
    {
        // Get all posts with images
        IQueryable<PostWithInteractions> postsQuery = db.PostsWithInteractions;

        if (feedConfig.RestrictToListUri != null)
        {
            var listMemberDids = await bsCache.GetListMembers(
                proto,
                new ATUri(feedConfig.RestrictToListUri),
                cancellationToken
            );
            var feedDids = listMemberDids.Select(s => s.Handler).ToList();
            postsQuery = postsQuery.Where(w => feedDids.Contains(w.Did));
        }

        var finalPostList = await postsQuery
            .Where(w =>
                w.ImageCount > 0
                && (w.LikeCount + w.QuotePostCount + w.ReplyCount + w.RepostCount)
                    >= MIN_INTERACTIONS
            )
            .OrderByDescending(o => o.LikeCount + o.QuotePostCount + o.ReplyCount + o.RepostCount)
            .Take(FEED_LIMIT)
            .ToListAsync(cancellationToken);

        if (finalPostList.Count < 1)
        {
            // Exit early
            return [];
        }

        IEnumerable<SortedFeedResult> sortedFeed;
        if (feedConfig.BalanceInteractions ?? false)
        {
            // Balance likes and reposts to the artist's follower count
            var authorDids = finalPostList
                .Select(s => s.GetAuthorDid())
                .DistinctBy(s => s.ToString())
                .ToArray();
            var authorProfiles = await bsCache.GetProfiles(proto, authorDids, cancellationToken);

            sortedFeed = finalPostList
                .Select(s => new
                {
                    Author = authorProfiles.Values.FirstOrDefault(f =>
                        f?.Did.ToString() == s.Did.ToString()
                    ),
                    Post = s,
                    InteractionCount = s.TotalInteractions,
                })
                .Where(w => w.Author != null)
                .Select(s => new SortedFeedResult(
                    s.Post,
                    (float)s.InteractionCount / s.Author!.FollowersCount
                ))
                .OrderByDescending(o => o.Score);
        }
        else
        {
            // Sort by likes
            sortedFeed = finalPostList
                .Select(s => new SortedFeedResult(s, s.TotalInteractions))
                .OrderByDescending(o => o.Score);
        }

        var feedOutput = sortedFeed
            .Select(s => new CustomSkeletonFeedPost(
                s.Post.ToUri(),
                FeedContext: $"Score: {s.Score}"
            ))
            .ToList();

        return feedOutput;
    }

    private record SortedFeedResult(IPostRecord Post, float Score);
}
