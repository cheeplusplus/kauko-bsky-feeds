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

[BskyFeed(nameof(TimelineMinusList), typeof(TimelineMinusListFeedConfig))]
public class TimelineMinusList(
    ILogger<TimelineMinusList> logger,
    ATProtocol proto,
    TimelineMinusListFeedConfig feedConfig,
    FeedDbContext db,
    BskyMetrics bskyMetrics,
    HybridCache mCache,
    IBskyCache bsCache,
    FeedInstanceMetadata feedMeta
) : IFeed
{
    private readonly ATDidComparer _atDidComparer = new();

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

        var followingList = await bsCache.GetFollowing(proto, proto.Session.Did, cancellationToken);
        var followingListStr = followingList.Select(s => s.Handler);
        var mutualsDids = await GetMutuals(cancellationToken);
        var listMemberDids = await bsCache.GetListMembers(
            proto,
            new ATUri(feedConfig.ListUri),
            cancellationToken
        );

        // Add any additional lists' members
        if (feedConfig.AdditionalLists?.Count > 0)
        {
            foreach (var listUri in feedConfig.AdditionalLists)
            {
                var moreListMemberDids = await bsCache.GetListMembers(
                    proto,
                    new ATUri(listUri),
                    cancellationToken
                );
                listMemberDids = listMemberDids.Concat(moreListMemberDids);
            }
        }

        listMemberDids = listMemberDids.Distinct().ToList();

        List<Post> posts;
        string? newCursor = null;
        if (feedConfig.FetchTimeline)
        {
            var postTlRes = await proto
                .Feed.GetTimelineAsync(cursor: cursor, cancellationToken: cancellationToken)
                .Record(bskyMetrics, "app.bsky.feed.getTimeline");
            var postTl = postTlRes.HandleResult();
            posts = (postTl?.Feed ?? [])
                .Where(w =>
                    ((feedConfig.AlwaysShowUserReposts?.Count ?? 0) > 0) || w.Reason == null
                )
                .Select(s => s.ToDbPost())
                .ToList();
            newCursor = postTl?.Cursor;
        }
        else
        {
            posts =
                await mCache.GetOrCreateAsync(
                    $"feed_db_{feedMeta.FeedUri}|{cursor}",
                    async (_) =>
                    {
                        return await db
                            .Posts.LatestFromCursor(cursor)
                            .Where(w => followingListStr.Contains(w.Did))
                            // use a large limit - assume we need to fetch more than we're going to show
                            .Take(100)
                            .ToListAsync(cancellationToken);
                    },
                    BskyCache.QuickOpts,
                    tags: ["feed", "feed/db", $"feed/{feedMeta.FeedUri}"],
                    cancellationToken: cancellationToken
                ) ?? [];
        }

        if (posts.Count < 1 || cancellationToken.IsCancellationRequested)
        {
            return new CustomSkeletonFeed([], null);
        }

        logger.LogDebug("Processing feed");
        var judgedFeed = posts.Select(s =>
        {
            PostJudgement judgement;
            try
            {
                judgement = JudgePost(
                    s,
                    requestor ?? proto.Session.Did,
                    feedConfig,
                    followingList,
                    mutualsDids,
                    listMemberDids
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to judge post {s}", s.ToAtUri());
                judgement = new PostJudgement(PostType.ErrorState, false);
            }

            return new InFeedJudgedPost(judgement, s.ToAtUri(), s.EventTime);
        });

        // We're doing this after judgement because at the moment, we're not reliably able to combine posts and reposts
        // If we were sure all PostReposts had a Post, we could write a view to fetch them all at once and give a Reason to JudgePost again
        // But since that's not super performant atm we'll just do it manually at this step
        List<InFeedJudgedPost> judgedReposts = [];
        if (!feedConfig.FetchTimeline && feedConfig.AlwaysShowUserReposts != null)
        {
            // Find the most recent set of reposts
            var reposts = await db
                .PostReposts.LatestFromCursor(cursor)
                .Where(w => feedConfig.AlwaysShowUserReposts.Contains(w.RepostDid))
                .OrderByDescending(o => o.EventTime)
                .Take(limit ?? 50)
                .ToListAsync(cancellationToken);
            judgedReposts = reposts
                .Select(s => new InFeedJudgedPost(
                    new PostJudgement(
                        PostType.Repost,
                        true,
                        RepostReason: new SkeletonReasonRepost(s.ToAtUri())
                    ),
                    s.ToParentPostAtUri(),
                    s.EventTime
                ))
                .ToList();
        }

        var filteredFeed = judgedFeed
            .Concat(judgedReposts)
            .Where(w => w.Judgement.ShouldShow)
            .OrderByDescending(o => o.Cursor)
            .Take(limit ?? 50)
            .ToList();
        var feedOutput = filteredFeed
            .Select(s => new SkeletonFeedPost(
                s.PostUri,
                s.Judgement.RepostReason,
                $"Reason: {s.Judgement.Type}"
            ))
            .ToList();

        newCursor ??= filteredFeed.LastOrDefault()?.Cursor.AsCursor();

        return new CustomSkeletonFeed(feedOutput, newCursor);
    }

    private async Task<IEnumerable<ATDid>> GetMutuals(CancellationToken cancellationToken = default)
    {
        if (proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        var followingList = await bsCache.GetFollowing(proto, proto.Session.Did, cancellationToken);
        var followersList = await bsCache.GetFollowers(proto, proto.Session.Did, cancellationToken);

        // For some reason the comparer isn't working so do this the hard way
        return followingList
            .Select(s => s.Handler)
            .Intersect(followersList.Select(s => s.Handler))
            .Select(ATDid.Create)
            .Where(w => w != null)
            .Cast<ATDid>()
            .ToList();
    }

    private PostJudgement JudgePost(
        Post post,
        ATDid requestorDid,
        TimelineMinusListFeedConfig feedConfig,
        IEnumerable<ATDid> followingDids,
        IEnumerable<ATDid> mutualsDids,
        IEnumerable<ATDid> listMemberDids
    )
    {
        bool isFollowing(ATDid? did) => did != null && followingDids.Contains(did, _atDidComparer);
        bool isMutual(ATDid? did) => did != null && mutualsDids.Contains(did, _atDidComparer);
        bool isInList(ATDid? did) => did != null && listMemberDids.Contains(did, _atDidComparer);

        bool isMuted(ATDid? did) =>
            did != null && (feedConfig.MuteUsers?.Contains(did.Handler) ?? false);

        // A user is visible if: we are following them, and they are: not in the list | a mutual | always shown
        // This check should only be used in cases where we care if we're following the person
        bool isVisible(ATDid? did) =>
            did != null
            && isFollowing(did)
            && (
                !isInList(did)
                || isMutual(did)
                || (feedConfig.AlwaysShowListUser?.Contains(did.Handler) ?? true)
            );

        var postAuthor = post.GetAuthorDid();

        // Self post
        if (feedConfig.ShowSelfPosts && _atDidComparer.Equals(postAuthor, requestorDid))
        {
            return new PostJudgement(PostType.SelfPost, true);
        }

        // Repost
        // TODO: We aren't ingesting these and they'd be in a different table anyway
        /*if (post.Reason != null)
        {
            // TODO: Add SkeletonReasonRepost to the PostJudgement - we need the orig URI which getTimeline doesn't seem to provide
            // Without the reason, they appear out of nowhere and are kinda confusing
            if (feedConfig.ShowReposts == ShowRepostsSetting.All)
            {
                return new PostJudgement(PostType.Repost, true);
            }
            else if (feedConfig.ShowReposts == ShowRepostsSetting.FollowingOnly)
            {
                if (isVisible(fvp.Post.Author.Did))
                {
                    return new PostJudgement(PostType.Repost, !isMuted(fvp.Post.Author.Did));
                }
            }

            return new PostJudgement(PostType.Repost, false);
        }*/

        // Reply
        if (post.ReplyParentUri != null)
        {
            var replyParentDid = post.GetReplyParentDid();
            var replyRootDid = post.GetReplyRootDid();

            if (feedConfig.ShowReplies == ShowRepliesSetting.All)
            {
                return new PostJudgement(PostType.Reply, true);
            }
            else if (
                feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnly
                || feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnlyTail
            )
            {
                if (
                    isVisible(replyParentDid)
                    && (
                        feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnlyTail
                        || isVisible(replyRootDid)
                    )
                )
                {
                    return new PostJudgement(
                        PostType.Reply,
                        !isMuted(replyParentDid) && !isMuted(replyRootDid)
                    );
                }
            }

            return new PostJudgement(PostType.Reply, false);
        }

        // Quote post
        if (post.EmbedRecordUri != null)
        {
            var embedRecordDid = post.GetEmbedRecordDid();

            if (feedConfig.ShowQuotePosts == ShowQuotePostsSetting.All)
            {
                return new PostJudgement(PostType.QuotePost, true);
            }
            else if (feedConfig.ShowQuotePosts == ShowQuotePostsSetting.FollowingOnly)
            {
                if (isVisible(embedRecordDid))
                {
                    return new PostJudgement(PostType.QuotePost, !isMuted(embedRecordDid));
                }
            }

            return new PostJudgement(PostType.QuotePost, false);
        }

        // Target list
        if (isInList(postAuthor))
        {
            if (feedConfig.AlwaysShowListUser?.Contains(post.Did) ?? false)
            {
                return new PostJudgement(PostType.InListAlwaysShow, !isMuted(postAuthor));
            }
            else if (feedConfig.IncludeListMutuals && isMutual(postAuthor))
            {
                return new PostJudgement(PostType.InListMutual, !isMuted(postAuthor));
            }

            return new PostJudgement(PostType.InList, false);
        }

        // Default to showing
        return new PostJudgement(PostType.Normal, !isMuted(postAuthor));
    }

    private record PostJudgement(
        PostType Type,
        bool ShouldShow,
        SkeletonReasonRepost? RepostReason = null
    );

    private record InFeedJudgedPost(PostJudgement Judgement, ATUri PostUri, DateTime Cursor);

    private enum PostType
    {
        SelfPost,
        Muted,
        Repost,
        Reply,
        QuotePost,
        InList,
        InListAlwaysShow,
        InListMutual,
        Normal,
        ContextDeleted,
        ErrorState,
    }
}
