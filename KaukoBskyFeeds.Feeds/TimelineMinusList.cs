using System.Data;
using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Post = KaukoBskyFeeds.Db.Models.Post;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(TimelineMinusList), typeof(TimelineMinusListFeedConfig))]
public class TimelineMinusList(
    ILogger<TimelineMinusList> logger,
    ATProtocol proto,
    TimelineMinusListFeedConfig feedConfig,
    FeedDbContext db,
    IBskyCache cache
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
        if (requestor == null)
        {
            // We need a requestor in order to reconstruct their feed
            // Defaulting to the session user is incorrect
            throw new FeedProhibitedException();
        }

        var followingList = await cache.GetFollowing(proto, proto.Session.Did, cancellationToken);
        var followingListStr = followingList.Select(s => s.Handler);
        var mutualsDids = await GetMutuals(cancellationToken);
        var listMemberDids = await cache.GetListMembers(
            proto,
            new ATUri(feedConfig.ListUri),
            cancellationToken
        );

        var posts = await db
            .Posts.LatestFromCursor(cursor)
            .Where(w => followingListStr.Contains(w.Did))
            // use a large limit - assume we need to fetch more than we're going to show
            .Take(100)
            .ToListAsync(cancellationToken);
        if (posts == null || posts.Count < 1 || cancellationToken.IsCancellationRequested)
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
                    requestor,
                    feedConfig,
                    followingList,
                    mutualsDids,
                    listMemberDids
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to judge post {s}", s);
                judgement = new PostJudgement(PostType.ErrorState, false);
            }

            return new
            {
                Judgement = judgement,
                PostUri = s.ToUri(),
                Cursor = s.EventTime,
            };
        });

        var filteredFeed = judgedFeed.Where(w => w.Judgement.ShouldShow).Take(limit ?? 50);
        var newCursor = filteredFeed.LastOrDefault()?.Cursor.AsCursor();
        var feedOutput = filteredFeed
            .Select(s => new CustomSkeletonFeedPost(
                s.PostUri,
                s.Judgement.RepostReason,
                $"Reason: {s.Judgement.Type}"
            ))
            .ToList();

        return new CustomSkeletonFeed(feedOutput, newCursor);
    }

    private async Task<IEnumerable<ATDid>> GetMutuals(CancellationToken cancellationToken = default)
    {
        if (proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        var followingList = await cache.GetFollowing(proto, proto.Session.Did, cancellationToken);
        var followersList = await cache.GetFollowers(proto, proto.Session.Did, cancellationToken);

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
