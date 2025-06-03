using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(BestArt), typeof(BestArtFeedConfig))]
public class BestArt(
    BestArtFeedConfig feedConfig,
    FeedInstanceMetadata feedMeta,
    FeedDbContext db,
    HybridCache mCache,
    IBskyCache bsCache
) : IFeed
{
    private int FeedLimit => feedConfig.FeedLimit;
    private int MinInteractions => feedConfig.MinInteractions;

    public BaseFeedConfig Config => feedConfig;

    public async Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        // Get all posts with images
        IQueryable<PostWithInteractions> postsQuery = db.PostsWithInteractions;

        if (feedConfig.RestrictToListUri != null)
        {
            var listMemberDids = await bsCache.GetListMembers(
                new ATUri(feedConfig.RestrictToListUri),
                cancellationToken
            );
            var feedDids = listMemberDids.Select(s => s.Handler).ToList();
            postsQuery = postsQuery.Where(w => feedDids.Contains(w.Did));
        }

        var finalPostList = await mCache.GetOrCreateAsync(
            $"feed_db_{feedMeta.FeedUri}",
            async (_) =>
            {
                return await postsQuery
                    .Where(w =>
                        w.ImageCount > 0
                        && (w.LikeCount + w.QuotePostCount + w.ReplyCount + w.RepostCount)
                            >= MinInteractions
                    )
                    .OrderByDescending(o =>
                        o.LikeCount + o.QuotePostCount + o.ReplyCount + o.RepostCount
                    )
                    .Take(FeedLimit)
                    .ToListAsync(cancellationToken);
            },
            BskyCache.DefaultOpts,
            tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
            cancellationToken: cancellationToken
        );

        if (finalPostList.Count < 1)
        {
            // Exit early
            return new CustomSkeletonFeed([], null);
        }

        IEnumerable<SortedFeedResult> sortedFeed;
        if (feedConfig.BalanceInteractions ?? false)
        {
            // Create a 'balanced' feed negating some factors like interactions, follower count, and post age
            var authorDids = finalPostList
                .Select(s => s.GetAuthorDid())
                .DistinctBy(s => s.ToString())
                .ToArray();
            var authorProfiles = await bsCache.GetProfiles(authorDids, cancellationToken);

            sortedFeed = finalPostList
                .Select(s => new
                {
                    Author = authorProfiles.Values.FirstOrDefault(f =>
                        f?.Did.ToString() == s.Did.ToString()
                    ),
                    Post = s,
                })
                .Where(w => w.Author != null)
                .Select(s => new SortedFeedResult(s.Post, ScorePostBalanced(s.Post, s.Author)))
                .OrderByDescending(o => o.Score);
        }
        else
        {
            // Sort by likes
            sortedFeed = finalPostList
                .Select(s => new SortedFeedResult(s, s.TotalInteractions))
                .OrderByDescending(o => o.Score);
        }

        if (limit.HasValue)
        {
            sortedFeed = sortedFeed.Take(limit.Value);
        }

        var feedOutput = sortedFeed
            .Select(s => new SkeletonFeedPost(s.Post.ToAtUri(), feedContext: $"Score: {s.Score}"))
            .ToList();

        return new CustomSkeletonFeed(feedOutput, null);
    }

    private static float ScorePostBalanced(PostWithInteractions post, ProfileViewDetailed? author)
    {
        // We're attempting to score a post in a 'fair' way that makes more things bubble up
        // where we don't just get things that are from popular artists or have had more time to get faves

        // Attempt to create a neutral score where the number of followers balances out the number of interactions
        // More interactions than followers is great
        var followerNeutralScore = (float)post.TotalInteractions / (author?.FollowersCount ?? 0);

        // Attempt to balance things out for time so we see newer stuff too
        // This shouldn't be as strong an influence as follower count
        var postAge = (DateTime.UtcNow - post.CreatedAt).TotalSeconds;
        var timeScore = 1 / (float)(postAge / TimeSpan.FromDays(3).TotalSeconds);
        var timeNeutralScore = followerNeutralScore * timeScore;

        return timeNeutralScore;
    }

    private record SortedFeedResult(IPostRecord Post, float Score);
}
