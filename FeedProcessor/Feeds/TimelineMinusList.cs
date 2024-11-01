using System.Data;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Bsky;

namespace KaukoBskyFeeds.FeedProcessor.Feeds;

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

        _logger.LogDebug("Processing feed");
        var filteredFeed = posts
            .Feed.Where(w =>
                // show own posts
                (
                    _feedConfig.ShowSelfPosts
                    && w.Post.Author.Did.Handler == _proto.Session?.Did?.Handler
                )
                || (
                    w.Reason == null // not a repost
                    && (
                        // not a reply, or a reply to someone we're following
                        w.Reply == null
                        || (
                            w.Reply?.Parent?.Author?.Did != null
                            && followingList.Contains(
                                w.Reply.Parent.Author.Did,
                                new ATDidComparer()
                            )
                        )
                    )
                    && followingList.Contains(w.Post.Author.Did, new ATDidComparer()) // someone we're following
                    && (
                        // not in the artist list, unless they're a mutual or in the always-show list
                        !listMemberDids.Contains(w.Post.Author.Did, new ATDidComparer())
                        || mutualsDids.Contains(w.Post.Author.Did, new ATDidComparer())
                        || (
                            _feedConfig.AlwaysShowListUser?.Contains(w.Post.Author.Did.Handler)
                            ?? false
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
