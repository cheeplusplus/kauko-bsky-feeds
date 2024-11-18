using System.Data;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(TimelineMinusList), typeof(TimelineMinusListFeedConfig))]
public class TimelineMinusList(
    ILogger<TimelineMinusList> logger,
    ATProtocol proto,
    TimelineMinusListFeedConfig feedConfig,
    IBskyCache cache
) : IFeed
{
    private readonly ATDidComparer _atDidComparer = new();

    public async Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogDebug("Fetching timeline");
        if (proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        if (feedConfig.RestrictToFeedOwner && !_atDidComparer.Equals(requestor, proto.Session.Did))
        {
            throw new FeedProhibitedException();
        }

        var postsRes = await proto.Feed.GetTimelineAsync(
            limit: 100, // assume we need to fetch more than we're going to show
            cursor: cursor,
            cancellationToken: cancellationToken
        );
        var posts = postsRes.HandleResult();
        if (posts == null || posts.Feed.Length < 1 || cancellationToken.IsCancellationRequested)
        {
            return new CustomSkeletonFeed([], posts?.Cursor ?? cursor);
        }

        var followingList = await cache.GetFollowing(proto, proto.Session.Did, cancellationToken);
        var mutualsDids = await GetMutuals(cancellationToken);
        var listMemberDids = await cache.GetListMembers(
            proto,
            new ATUri(feedConfig.ListUri),
            cancellationToken
        );

        logger.LogDebug("Processing feed");
        var judgedFeed = posts.Feed.Select(s =>
        {
            PostJudgement judgement;
            try
            {
                judgement = JudgePost(s, feedConfig, followingList, mutualsDids, listMemberDids);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to judge post {s}", s);
                judgement = new PostJudgement(PostType.ErrorState, false);
            }

            return new { Judgement = judgement, PostUri = s.Post.Uri.ToString() };
        });

        var filteredFeed = judgedFeed
            .Where(w => w.Judgement.ShouldShow)
            .Select(s => new CustomSkeletonFeedPost(
                s.PostUri,
                s.Judgement.RepostReason,
                $"Reason: {s.Judgement.Type}"
            ));

        if (limit.HasValue)
        {
            // cap at requested limit
            filteredFeed = filteredFeed.TakeLast(limit.Value);
        }

        return new CustomSkeletonFeed(filteredFeed.ToList(), posts.Cursor);
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
        FeedViewPost fvp,
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

        // Self post
        if (feedConfig.ShowSelfPosts && fvp.Post.Author.Did.Handler == proto.Session?.Did?.Handler)
        {
            return new PostJudgement(PostType.SelfPost, true);
        }

        // Repost
        if (fvp.Reason != null)
        {
            // TODO: Add SkeletonReasonRepost to the PostJudgement - we need the orig URI which getTimeline doesn't seem to provide
            // Without the reason, they appear out of nowhere and are kinda confusing
            /*if (feedConfig.ShowReposts == ShowRepostsSetting.All)
            {
                return new PostJudgement(PostType.Repost, true);
            }
            else if (feedConfig.ShowReposts == ShowRepostsSetting.FollowingOnly)
            {
                if (isVisible(fvp.Post.Author.Did))
                {
                    return new PostJudgement(PostType.Repost, !isMuted(fvp.Post.Author.Did));
                }
            }*/

            return new PostJudgement(PostType.Repost, false);
        }

        // Reply
        if (fvp.Reply != null)
        {
            if (fvp.Reply?.Parent?.Author == null || fvp.Reply?.Root?.Author == null)
            {
                return new PostJudgement(PostType.ContextDeleted, false);
            }

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
                    isVisible(fvp.Reply?.Parent?.Author.Did)
                    && (
                        feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnlyTail
                        || isVisible(fvp.Reply?.Root?.Author.Did)
                    )
                )
                {
                    return new PostJudgement(
                        PostType.Reply,
                        !isMuted(fvp.Reply?.Parent?.Author.Did)
                            && !isMuted(fvp.Reply?.Root?.Author.Did)
                    );
                }
            }

            return new PostJudgement(PostType.Reply, false);
        }

        // Quote post
        if (fvp.Post.Embed is RecordViewEmbed re)
        {
            if (feedConfig.ShowQuotePosts == ShowQuotePostsSetting.All)
            {
                return new PostJudgement(PostType.QuotePost, true);
            }
            else if (feedConfig.ShowQuotePosts == ShowQuotePostsSetting.FollowingOnly)
            {
                if (isVisible(re.Record.Author.Did))
                {
                    return new PostJudgement(PostType.QuotePost, !isMuted(re.Record.Author.Did));
                }
            }

            return new PostJudgement(PostType.QuotePost, false);
        }

        // Target list
        if (isInList(fvp.Post.Author.Did))
        {
            if (feedConfig.AlwaysShowListUser?.Contains(fvp.Post.Author.Did.Handler) ?? false)
            {
                return new PostJudgement(PostType.InListAlwaysShow, !isMuted(fvp.Post.Author.Did));
            }
            else if (feedConfig.IncludeListMutuals && isMutual(fvp.Post.Author.Did))
            {
                return new PostJudgement(PostType.InListMutual, !isMuted(fvp.Post.Author.Did));
            }

            return new PostJudgement(PostType.InList, false);
        }

        // Default to showing
        return new PostJudgement(PostType.Normal, !isMuted(fvp.Post.Author.Did));
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
