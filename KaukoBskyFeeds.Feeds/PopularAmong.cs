using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Embed;
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

[BskyFeed(nameof(PopularAmong), typeof(PopularAmongFeedConfig))]
public class PopularAmong(
    ATProtocol proto,
    PopularAmongFeedConfig feedConfig,
    FeedDbContext db,
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

        var targetDids = feedConfig.TargetGroup switch
        {
            PopularAmongGroupSetting.Followers => await bsCache.GetFollowers(
                proto,
                requestor ?? proto.Session.Did,
                cancellationToken
            ),
            PopularAmongGroupSetting.Following => await bsCache.GetFollowing(
                proto,
                requestor ?? proto.Session.Did,
                cancellationToken
            ),
            PopularAmongGroupSetting.Mutuals => await bsCache.GetMutuals(
                proto,
                requestor ?? proto.Session.Did,
                cancellationToken
            ),
            _ => throw new Exception("Invalid target group"),
        };
        var targetListStr = targetDids.Select(s => s.Handler).ToList();

        var allInteractions = await mCache.GetOrCreateAsync(
            $"feed_db_{feedMeta.FeedUri}",
            async (_) =>
            {
                var qLikes = db
                    .PostLikes.Where(w => targetListStr.Contains(w.LikeDid))
                    .Select(s => new
                    {
                        Interactor = s.LikeDid,
                        Did = s.ParentDid,
                        Rkey = s.ParentRkey,
                    });
                var qReposts = db
                    .PostReposts.Where(w => targetListStr.Contains(w.RepostDid))
                    .Select(s => new
                    {
                        Interactor = s.RepostDid,
                        Did = s.ParentDid,
                        Rkey = s.ParentRkey,
                    });
                var qQuotePosts = db
                    .PostQuotePosts.Where(w => targetListStr.Contains(w.QuoteDid))
                    .Select(s => new
                    {
                        Interactor = s.QuoteDid,
                        Did = s.ParentDid,
                        Rkey = s.ParentRkey,
                    });
                var qJoined = qLikes.Union(qReposts).Union(qQuotePosts).Distinct();
                var qFinal = qJoined
                    .GroupBy(g => new { g.Did, g.Rkey })
                    .Select(s => new
                    {
                        Interactions = s.Count(),
                        s.Key.Did,
                        s.Key.Rkey,
                    });
                if (feedConfig is { ImagesOnly: true, ImagesChecksServer: false })
                {
                    var qPosts = db.Posts.Where(w => w.ImageCount > 0);
                    qFinal = qFinal.Join(
                        qPosts,
                        ok => new { ok.Did, ok.Rkey },
                        ik => new { ik.Did, ik.Rkey },
                        (outer, inner) => outer
                    );
                }

                qFinal = qFinal
                    .Where(w => w.Interactions >= feedConfig.MinRelevance)
                    .OrderByDescending(o => o.Interactions)
                    .Take(limit ?? 50);

                // var req = await q.ToListAsync(cancellationToken);
                var req = await qFinal.ToListAsync(cancellationToken);
                return req.Select(s => new
                    {
                        Count = s.Interactions,
                        Ref = new PostRecordRef(s.Did, s.Rkey),
                    })
                    .Select(s => new { s.Count, s.Ref });
            },
            BskyCache.DefaultOpts,
            tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
            cancellationToken: cancellationToken
        );
        var allInteractionsUseful = allInteractions
            .Select(s => new
            {
                s.Count,
                s.Ref,
                PostUri = s.Ref.ToAtUri(BskyConstants.CollectionTypePost),
            })
            .ToList();

        if (feedConfig is { ImagesOnly: true, ImagesChecksServer: true })
        {
            // Fetch and filter posts from the server, to if they have image embeds
            var posts = await bsCache.GetPosts(
                proto,
                allInteractionsUseful.Select(s => s.PostUri),
                cancellationToken
            );
            var postsWithImages = posts
                .Where(w =>
                    w?.Embed
                        is ViewImages { Images.Count: > 0 }
                            or ViewRecordWithMedia { Media: ViewImages { Images.Count: > 0 } }
                )
                .Select(s => new PostRecordRef(s?.Uri.Did?.ToString() ?? "", s?.Uri.Rkey ?? ""));
            allInteractionsUseful = allInteractionsUseful
                .Where(w => postsWithImages.Contains(w.Ref))
                .ToList();
        }

        var filteredFeed = allInteractionsUseful
            .OrderByDescending(o => o.Count)
            .Select(s => new SkeletonFeedPost(s.PostUri, feedContext: $"{s.Count} interactions"));

        return new CustomSkeletonFeed(filteredFeed.ToList(), null);
    }
}
