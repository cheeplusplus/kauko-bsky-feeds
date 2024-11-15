using System.Data;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Feeds;

public class TimelineMinusList : IFeed
{
    private readonly ILogger<TimelineMinusList> _logger;
    private readonly ATProtocol _proto;
    private readonly TimelineMinusListFeedConfig _feedConfig;
    private readonly IBskyCache _cache;

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

        var didComparer = new ATDidComparer();
        bool isFollowing(ATDid? did) => did != null && followingList.Contains(did, didComparer);
        bool isMutual(ATDid? did) => did != null && mutualsDids.Contains(did, didComparer);
        bool isInList(ATDid? did) => did != null && listMemberDids.Contains(did, didComparer);
        bool isMuted(ATDid? did) =>
            did != null && (_feedConfig.MuteUsers?.Contains(did.Handler) ?? false);

        _logger.LogDebug("Processing feed");
        var filteredFeed = posts
            .Feed.Where(w =>
                // show own posts
                (
                    _feedConfig.ShowSelfPosts
                    && w.Post.Author.Did.Handler == _proto.Session?.Did?.Handler
                )
                || (
                    // followingList.Contains(w.Post.Author.Did, new ATDidComparer()) // someone we're following
                    !isMuted(w.Post.Author.Did) // drop muted users
                    && (
                        // not a repost
                        _feedConfig.ShowReposts || (!_feedConfig.ShowReposts && w.Reason == null)
                    )
                    && (
                        // not a reply, or a reply to someone we're following
                        w.Reply == null
                        || _feedConfig.ShowReplies == ShowRepliesSetting.All
                        || (
                            _feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnlyTail
                            && isFollowing(w.Reply.Parent?.Author.Did)
                            && !isMuted(w.Reply.Parent?.Author.Did)
                        )
                        || (
                            _feedConfig.ShowReplies == ShowRepliesSetting.FollowingOnly
                            && isFollowing(w.Reply?.Parent?.Author.Did)
                            && isFollowing(w.Reply?.Root?.Author.Did)
                            && !isMuted(w.Reply?.Parent?.Author.Did)
                            && !isMuted(w.Reply?.Root?.Author.Did)
                        )
                    )
                    && (
                        // not a quote post, or a quote post to someone we're following
                        _feedConfig.ShowQuotePosts == ShowQuotePostsSetting.All
                        || (
                            _feedConfig.ShowQuotePosts == ShowQuotePostsSetting.FollowingOnly
                            && (
                                w.Post.Record?.Embed == null
                                || (
                                    w.Post.Record.Embed is RecordViewEmbed re
                                    && isFollowing(re.Record.Author.Did)
                                    && !isMuted(re.Record.Author.Did)
                                )
                            )
                        )
                    )
                    && (
                        // not in the artist list, unless they're a mutual or in the always-show list
                        !isInList(w.Post.Author.Did)
                        || (
                            (_feedConfig.IncludeListMutuals && isMutual(w.Post.Author.Did))
                            || (
                                _feedConfig.AlwaysShowListUser?.Contains(w.Post.Author.Did.Handler)
                                ?? false
                            )
                        )
                    )
                )
            )
            .Select(s => new SkeletonFeedPost(s.Post.Uri.ToString()));

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
}
