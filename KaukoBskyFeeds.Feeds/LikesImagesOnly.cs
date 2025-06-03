using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(LikesImagesOnly), typeof(LikesImagesOnlyFeedConfig))]
public class LikesImagesOnly(
    LikesImagesOnlyFeedConfig feedConfig,
    FeedInstanceMetadata feedMeta,
    FeedDbContext db,
    IBskyApi api,
    HybridCache mCache,
    IBskyCache bsCache
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
        api.AssertLogin();
        if (requestor == null)
        {
            throw new FeedProhibitedException();
        }

        // This only looks at the past 50 likes, which is not ideal
        // TODO: Support cursors and pagination here
        // It'll be kind of complex since not every like has an image so we'll probably want to page ~50 at a time
        var likesUris = (await bsCache.GetLikes(requestor, cancellationToken)).ToList();

        List<Db.Models.Post> posts;
        if (feedConfig.FetchTimeline)
        {
            var hydratedPosts = await bsCache.GetPosts(likesUris, cancellationToken);
            posts = hydratedPosts
                .WhereNotNull()
                .Select(s => s.ToDbPost())
                .Where(w => w.ImageCount > 0)
                .AsQueryable()
                .ToList();
        }
        else
        {
            var likesSimpleUris = likesUris.Select(s => s.Did + "/" + s.Rkey);
            var dbPosts = await mCache.GetOrCreateAsync(
                $"feed_db_{feedMeta.FeedUri}||{requestor}",
                async (_) =>
                {
                    return await db
                        .Posts.Where(w =>
                            likesSimpleUris.Contains(w.Did + "/" + w.Rkey) && w.ImageCount > 0
                        )
                        .ToListAsync(cancellationToken);
                },
                // match cache time on GetLikes
                BskyCache.DefaultOpts,
                tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
                cancellationToken: cancellationToken
            );

            // Put them back in the original order
            posts = likesUris
                .Select(s => dbPosts.SingleOrDefault(w => w.ToAtUri().ToString() == s.ToString()))
                .WhereNotNull()
                .ToList();
        }

        var filteredFeed = posts.Select(s => new SkeletonFeedPost(s.ToAtUri()));

        return new CustomSkeletonFeed(filteredFeed.ToList(), null);
    }
}
