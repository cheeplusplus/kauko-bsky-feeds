using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FishyFlip;
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
            PopularAmongGroupSetting.Mutuals => await GetMutuals(
                requestor ?? proto.Session.Did,
                cancellationToken
            ),
            _ => throw new Exception("Invalid target group"),
        };
        var targetListStr = targetDids.Select(s => s.Handler).ToList();

        var allInteractions =
            await mCache.GetOrCreateAsync(
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
                        .Select(s => new
                        {
                            s.Count,
                            s.Ref,
                            PostUri = s.Ref.ToUri(BskyConstants.COLLECTION_TYPE_POST),
                        });
                },
                BskyCache.DEFAULT_OPTS,
                tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
                cancellationToken: cancellationToken
            ) ?? [];

        if (feedConfig is { ImagesOnly: true, ImagesChecksServer: true })
        {
            var ilist = allInteractions.ToList();
            // Fetch and filter posts from the server, to if they have image embeds
            var posts = await bsCache.GetPosts(
                proto,
                ilist.Select(s => ATUri.Create(s.PostUri)),
                cancellationToken
            );
            var postsWithImages = posts
                .Where(w =>
                    w?.Embed
                        is ImageViewEmbed { Images.Length: > 0 }
                            or RecordWithMediaViewEmbed
                            {
                                Embed: ImagesEmbed { Images.Length: > 0 }
                            }
                )
                .Select(s => new PostRecordRef(s?.Uri.Did?.ToString() ?? "", s?.Uri.Rkey ?? ""));
            allInteractions = ilist.Where(w => postsWithImages.Contains(w.Ref));
        }

        var filteredFeed = allInteractions
            .OrderByDescending(o => o.Count)
            .Select(s => new CustomSkeletonFeedPost(
                s.PostUri,
                FeedContext: $"{s.Count} interactions"
            ));

        return new CustomSkeletonFeed(filteredFeed.ToList(), null);
    }

    private async Task<IEnumerable<ATDid>> GetMutuals(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        var followingList = await bsCache.GetFollowing(proto, user, cancellationToken);
        var followersList = await bsCache.GetFollowers(proto, user, cancellationToken);

        return followingList
            .Select(s => s.Handler)
            .Intersect(followersList.Select(s => s.Handler))
            .Select(ATDid.Create)
            .Where(w => w != null)
            .Cast<ATDid>()
            .ToList();
    }

    private class InteractionQueryResult
    {
        [Required]
        [Column("interactions")]
        public required int Interactions { get; set; }

        [Required]
        [Column("did")]
        public required string Did { get; set; }

        [Required]
        [Column("rkey")]
        public required string Rkey { get; set; }
    }
}
