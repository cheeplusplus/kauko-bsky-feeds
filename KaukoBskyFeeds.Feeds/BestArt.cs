using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Redis;
using KaukoBskyFeeds.Redis.FastStore;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Post = KaukoBskyFeeds.Db.Models.Post;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(BestArt), typeof(BestArtFeedConfig))]
public class BestArt(
    ATProtocol proto,
    BestArtFeedConfig feedConfig,
    FeedDbContext db,
    IFastStore fastStore,
    HybridCache hCache,
    IBskyCache bsCache
) : IFeed
{
    private const int FEED_LIMIT = 50;
    private const int DB_PAGE_SIZE = 50;
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
        return await hCache.GetOrCreateAsync(
                $"BestArtFeed|list_{feedConfig.RestrictToListUri ?? "all"}|bal_{feedConfig.BalanceInteractions ?? false}",
                async (opts) =>
                {
                    return await GetFeedSkeletonUncached(cancellationToken);
                },
                BskyCache.HYBRID_CACHE_OPTS
            ) ?? new CustomSkeletonFeed([], null);
    }

    private async Task<CustomSkeletonFeed> GetFeedSkeletonUncached(
        CancellationToken cancellationToken = default
    )
    {
        // Get all posts with images
        var postsQuery = db.Posts.Where(w =>
            w.Embeds != null && w.Embeds.Images != null && w.Embeds.Images.Count > 0
        );

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

        // Page through the database
        List<PostIdealizer> postsZipped = [];
        IEnumerable<Post> postPage;
        do
        {
            postPage = await postsQuery.Take(DB_PAGE_SIZE).ToListAsync(cancellationToken);

            await Parallel.ForEachAsync(
                postPage,
                cancellationToken,
                async (post, ct) =>
                {
                    var ic = await fastStore.GetTotalInteractionCount(post.ToAtUri().ToString());
                    if (ic >= MIN_INTERACTIONS)
                    {
                        postsZipped.Add(new PostIdealizer(post, ic));
                    }
                }
            );

            postsQuery = postsQuery.Skip(DB_PAGE_SIZE);
        } while (postPage.Count() >= DB_PAGE_SIZE && postsZipped.Count < FEED_LIMIT);

        if (postsZipped.Count < 1)
        {
            // Exit early
            return new CustomSkeletonFeed([], null);
        }

        // Cut to the final candidates before performing sorting
        var finalPostList = postsZipped.Take(FEED_LIMIT).ToList();

        IEnumerable<SortedFeedResult> sortedFeed;
        if (feedConfig.BalanceInteractions ?? false)
        {
            // Balance likes and reposts to the artist's follower count
            var authorDids = finalPostList.Select(s => s.AuthorDid).Distinct().ToArray();
            var authorsHydratedReq = await proto.Actor.GetProfilesAsync(
                authorDids,
                cancellationToken
            );
            var authorsHydrated = authorsHydratedReq.HandleResult();

            var authorsZipped = finalPostList
                .Select(s => new
                {
                    Author = authorsHydrated?.Profiles?.SingleOrDefault(h =>
                        h.Did.ToString() == s.AuthorDid.ToString()
                    ),
                    s.Post,
                    s.InteractionCount,
                })
                .Where(w => w.Author != null);

            sortedFeed = authorsZipped
                .Select(s => new SortedFeedResult(
                    s.Post,
                    s.InteractionCount / s.Author!.FollowersCount
                ))
                .OrderBy(o => o.Score);
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
