using System.Runtime.CompilerServices;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
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
using Post = KaukoBskyFeeds.Db.Models.Post;

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
    private const int FEED_LIMIT = 25; // restriction on artist did fetch
    private const int MIN_INTERACTIONS = 3;

    public BaseFeedConfig Config => feedConfig;

    public async Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        // TODO: Don't pass the limit into the uncached func, always fetch with max and limit after the cache
        return await mCache.GetOrCreateAsync(
                $"BestArtFeed|list_{feedConfig.RestrictToListUri ?? "all"}|bal_{feedConfig.BalanceInteractions ?? false}",
                async (opts) =>
                {
                    return await GetFeedSkeletonUncached(cancellationToken);
                },
                BskyCache.DEFAULT_OPTS
            ) ?? new CustomSkeletonFeed([], null);
    }

    private async Task<CustomSkeletonFeed> GetFeedSkeletonUncached(
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

        postsQuery = postsQuery
            .Where(w => (w.LikeCount + w.ReplyCount + w.RepostCount + w.QuotePostCount) > 3)
            .OrderByDescending(o => o.LikeCount + o.ReplyCount + o.RepostCount + o.QuotePostCount)
            .Take(FEED_LIMIT);

        var finalPostList = await postsQuery
            .Select(s => new PostIdealizer(s, s.TotalInteractions))
            .ToListAsync(cancellationToken);

        if (finalPostList.Count < 1)
        {
            // Exit early
            return new CustomSkeletonFeed([], null);
        }

        IEnumerable<SortedFeedResult> sortedFeed;
        if (feedConfig.BalanceInteractions ?? false)
        {
            // Balance likes and reposts to the artist's follower count
            var authorDids = finalPostList
                .Select(s => s.AuthorDid)
                .DistinctBy(s => s.ToString())
                .ToArray();
            var authorsHydratedReq = await proto.Actor.GetProfilesAsync(
                authorDids,
                cancellationToken
            );
            var authorsHydrated = authorsHydratedReq.HandleResult();

            sortedFeed = finalPostList
                .Select(s => new
                {
                    Author = authorsHydrated?.Profiles?.SingleOrDefault(h =>
                        h.Did.ToString() == s.AuthorDid.ToString()
                    ),
                    s.Post,
                    s.InteractionCount,
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
                .Select(s => new SortedFeedResult(s.Post, s.InteractionCount))
                .OrderByDescending(o => o.Score);
        }

        var feedOutput = sortedFeed.Select(s => new CustomSkeletonFeedPost(
            s.Post.ToUri(),
            FeedContext: $"Score: {s.Score}"
        ));

        return new CustomSkeletonFeed(feedOutput.ToList(), null);
    }

    record PostIdealizer(Post Post, long InteractionCount)
    {
        public ATDid AuthorDid => Post.GetAuthorDid();
    }

    record SortedFeedResult(Post Post, float Score);
}
