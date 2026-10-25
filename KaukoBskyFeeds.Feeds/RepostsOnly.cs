using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Interactions;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(RepostsOnly), typeof(RepostsOnlyFeedConfig), true)]
public class RepostsOnly(
    RepostsOnlyFeedConfig feedConfig,
    FeedInstanceMetadata feedMeta,
    FeedDbContext db,
    IBskyApi api,
    HybridCache mCache,
    IBskyCache bsCache,
    IInteractionCache iaCache
) : BaseFeed(feedMeta.FeedUri, feedConfig)
{
    public override async Task<CustomSkeletonFeed> GetFeedSkeleton(
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

        IEnumerable<string> followingListStr;
        if (feedConfig.OnlyShowUsers != null)
        {
            followingListStr = feedConfig.OnlyShowUsers;
        }
        else
        {
            var followingList = await bsCache.GetFollowing(requestor, cancellationToken);
            followingListStr = followingList.Select(s => s.ToString());
        }

        string? newCursor;
        List<RepostData> posts;
        if (feedConfig.FetchTimeline && feedConfig.RestrictToFeedOwner)
        {
            var postTl = await api.GetTimeline(
                cursor: cursor,
                cancellationToken: cancellationToken
            );
            posts = (postTl?.Feed ?? [])
                .Where(w =>
                    w.Reason is ReasonRepost rrp
                    && followingListStr.Contains(rrp.By.Did.ToString())
                    && (
                        !feedConfig.ExcludeSelfReposts
                        || rrp.By.Did.ToString() != w.Post.Author.Did.ToString()
                    )
                )
                .Select(s => new RepostData(
                    s.Post.IndexedAt ?? DateTime.MinValue,
                    new PostRecordRef(
                        (s.Reason as ReasonRepost)!.By.Did.ToString(),
                        (s.Reason as ReasonRepost)!.Uri!.Rkey
                    ),
                    new PostRecordRef(s.Post.Author.Did.ToString(), s.Post.Uri.Rkey)
                ))
                .ToList();
            newCursor = postTl?.Cursor;
        }
        else
        {
            var postDb = await mCache.GetOrCreateAsync(
                $"feed_db_{feedMeta.FeedUri}||{requestor}|{cursor}",
                async (ct) =>
                {
                    return await db
                        .PostReposts.LatestFromCursor(cursor)
                        .Where(w =>
                            followingListStr.Contains(w.RepostDid)
                            && (!feedConfig.ExcludeSelfReposts || w.ParentDid != w.RepostDid)
                        )
                        .OrderByDescending(o => o.EventTime)
                        .Take(limit ?? 50)
                        .ToListAsync(ct);
                },
                BskyCache.QuickOpts,
                tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
                cancellationToken: cancellationToken
            );

            posts = postDb.Select(s => new RepostData(s.EventTime, s.ParentRef, s.Ref)).ToList();

            newCursor = posts.LastOrDefault()?.AsCursor();
        }

        // Due to how feed interactions work, we'll only filter out specific reposts
        var opinions = await iaCache.GetInteractions(feedMeta.FeedUri, requestor);
        var skipPosts = opinions
            .Where(kvp => kvp.Value.Contains(BskyConstants.InteractionRequestLess))
            .Select(kvp => kvp.Key)
            .ToList();

        var filteredFeed = posts
            .Where(w => !skipPosts.Contains(w.Repost.Did))
            .DistinctBy(d => d.Original) // only show one per
            .OrderByDescending(o => o.RepostTime)
            .Take(limit ?? 50)
            .ToList();
        var feedOutput = filteredFeed
            .Select(s => new SkeletonFeedPost(
                s.Original.ToAtUri(BskyConstants.CollectionTypePost),
                reason: new SkeletonReasonRepost(
                    s.Repost.ToAtUri(BskyConstants.CollectionTypeRepost)
                ),
                feedContext: this.GetStandardFeedContext(null)
            ))
            .ToList();

        return new CustomSkeletonFeed(feedOutput, newCursor);
    }

    private record RepostData(DateTime RepostTime, PostRecordRef Original, PostRecordRef Repost)
    {
        public string AsCursor() => RepostTime.AsCursor();
    }
}
