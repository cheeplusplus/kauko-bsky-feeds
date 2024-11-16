using System.Data;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Feeds;

public class TimelineMinusList : IFeed
{
    private readonly ILogger<TimelineMinusList> _logger;
    private readonly ATProtocol _proto;
    private readonly TimelineMinusListFeedConfig _feedConfig;
    private readonly IBskyCache _cache;
    private readonly ATDidComparer _atDidComparer = new();

    public TimelineMinusList(
        ILogger<TimelineMinusList> logger,
        ATProtocol proto,
        TimelineMinusListFeedConfig feedConfig,
        IBskyCache cache
    )
    {
        _logger = logger;
        _proto = proto;
        _feedConfig = feedConfig;
        _cache = cache;
        DisplayName = feedConfig.DisplayName;
        Description = feedConfig.Description;
    }

    public string DisplayName { get; init; }

    public string Description { get; init; }

    public async Task<SkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("Fetching timeline");
        if (_proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        if (
            _feedConfig.RestrictToFeedOwner && !_atDidComparer.Equals(requestor, _proto.Session.Did)
        )
        {
            throw new FeedProhibitedException();
        }

        var postsRes = await _proto.Feed.GetTimelineAsync(
            limit: 100, // assume we need to fetch more than we're going to show
            cursor: cursor,
            cancellationToken: cancellationToken
        );
        var posts = postsRes.HandleResult();
        if (posts == null || posts.Feed.Length < 1 || cancellationToken.IsCancellationRequested)
        {
            return new SkeletonFeed([], posts?.Cursor ?? cursor);
        }

        var followingList = await _cache.GetFollowing(
            _proto,
            _proto.Session.Did,
            cancellationToken
        );
        var mutualsDids = await GetMutuals(cancellationToken);
        var listMemberDids = await _cache.GetListMembers(
            _proto,
            new ATUri(_feedConfig.ListUri),
            cancellationToken
        );

        _logger.LogDebug("Processing feed");
        var judgedFeed = posts.Feed.Select(s => new
        {
            Judgement = JudgePost(s, followingList, mutualsDids, listMemberDids),
            PostUri = s.Post.Uri.ToString(),
        });

        var filteredFeed = judgedFeed
            .Where(w => w.Judgement.ShouldShow)
            .Select(s => new SkeletonFeedPost(s.PostUri, s.Judgement.RepostReason));

        if (limit.HasValue)
        {
            // cap at requested limit
            filteredFeed = filteredFeed.TakeLast(limit.Value);
        }

        return new SkeletonFeed(filteredFeed.ToArray(), posts.Cursor);
    }

    private async Task<IEnumerable<ATDid>> GetMutuals(CancellationToken cancellationToken = default)
    {
        if (_proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        var followingList = await _cache.GetFollowing(
            _proto,
            _proto.Session.Did,
            cancellationToken
        );
        var followersList = await _cache.GetFollowers(
            _proto,
            _proto.Session.Did,
            cancellationToken
        );

        return followingList.Intersect(followersList, new ATDidComparer());
    }

    private PostJudgement JudgePost(
        FeedViewPost fvp,
        IEnumerable<ATDid> followingDids,
        IEnumerable<ATDid> mutualsDids,
        IEnumerable<ATDid> listMemberDids
    )
    {
        bool isFollowing(ATDid? did) => did != null && followingDids.Contains(did, _atDidComparer);
        bool isMutual(ATDid? did) => did != null && mutualsDids.Contains(did, _atDidComparer);
        bool isInList(ATDid? did) => did != null && listMemberDids.Contains(did, _atDidComparer);
        bool isMuted(ATDid? did) =>
            did != null && (_feedConfig.MuteUsers?.Contains(did.Handler) ?? false);

        // Self post
        if (
            _feedConfig.ShowSelfPosts
            && fvp.Post.Author.Did.Handler == _proto.Session?.Did?.Handler
        )
        {
            return new PostJudgement(PostType.SelfPost, true);
        }

        // Repost
        if (fvp.Reason != null)
        {
            // TODO: Add SkeletonReasonRepost to the PostJudgement - we need the orig URI which getTimeline doesn't seem to provide
            // Without the reason, they appear out of nowhere and are kinda confusing
            /*if (_feedConfig.ShowReposts == ShowRepostsSetting.All)
            {
                return new PostJudgement(PostType.Repost, true);
            }
            else if (_feedConfig.ShowReposts == ShowRepostsSetting.FollowingOnly)
            {
                if (
                    isFollowing(fvp.Post.Author.Did)
                    && (!isInList(fvp.Post.Author.Did) || isMutual(fvp.Post.Author.Did))
                )
                {
                    return new PostJudgement(PostType.Repost, !isMuted(fvp.Post.Author.Did));
                }
            }*/

            return new PostJudgement(PostType.Repost, false);
        }

        // Reply
        if (fvp.Reply != null)
        {
            if (_feedConfig.ShowReplies == ShowRepliesSetting.All)
            {
                return new PostJudgement(PostType.Reply, true);
            }
            else if (_feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnly)
            {
                if (isFollowing(fvp.Reply.Parent?.Author.Did))
                {
                    return new PostJudgement(
                        PostType.Reply,
                        !isMuted(fvp.Reply.Parent?.Author.Did)
                    );
                }
            }
            else if (_feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnlyTail)
            {
                if (
                    isFollowing(fvp.Reply?.Parent?.Author.Did)
                    && isFollowing(fvp.Reply?.Root?.Author.Did)
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
        if (fvp.Post.Record?.Embed is RecordViewEmbed re)
        {
            if (_feedConfig.ShowQuotePosts == ShowQuotePostsSetting.All)
            {
                return new PostJudgement(PostType.QuotePost, true);
            }
            else if (_feedConfig.ShowQuotePosts == ShowQuotePostsSetting.FollowingOnly)
            {
                if (isFollowing(re.Record.Author.Did))
                {
                    return new PostJudgement(PostType.QuotePost, !isMuted(re.Record.Author.Did));
                }
            }

            return new PostJudgement(PostType.QuotePost, false);
        }

        // Target list
        if (isInList(fvp.Post.Author.Did))
        {
            if (_feedConfig.AlwaysShowListUser?.Contains(fvp.Post.Author.Did.Handler) ?? false)
            {
                return new PostJudgement(PostType.InListAlwaysShow, !isMuted(fvp.Post.Author.Did));
            }
            else if (_feedConfig.IncludeListMutuals && isMutual(fvp.Post.Author.Did))
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
        NotFollowing,
        SelfPost,
        Muted,
        Repost,
        Reply,
        QuotePost,
        InList,
        InListAlwaysShow,
        InListMutual,
        Normal,
    }
}
