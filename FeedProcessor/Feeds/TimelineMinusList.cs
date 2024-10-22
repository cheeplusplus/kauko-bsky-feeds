using System.Data;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;

namespace KaukoBskyFeeds.FeedProcessor.Feeds;

public class TimelineMinusList : IFeed
{
    private readonly ILogger<TimelineMinusList> _logger;
    private readonly ATProtocol _proto;
    private readonly TimelineMinusListFeedConfig _feedConfig;
    private DateTime _lastMutualsUpdate = DateTime.MinValue;
    private List<ATDid> _followingList = [];
    private List<ATDid> _followersList = [];
    private DateTime _lastListMemberUpdate = DateTime.MinValue;
    private List<ATDid> _listMembers = [];

    public TimelineMinusList(
        ILogger<TimelineMinusList> logger,
        ATProtocol proto,
        TimelineMinusListFeedConfig feedConfig
    )
    {
        _logger = logger;
        _proto = proto;
        _feedConfig = feedConfig;
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
        var postsRes = await _proto.Feed.GetTimelineAsync(
            cursor: cursor,
            cancellationToken: cancellationToken
        );
        var posts = postsRes.HandleResult();
        if (posts == null || cancellationToken.IsCancellationRequested)
        {
            return new SkeletonFeed([], cursor);
        }

        var mutualsDids = await UpdateMutuals(cancellationToken);
        var listMemberDids = await UpdateListMembers(cancellationToken);

        _logger.LogDebug("Processing feed");
        var filteredFeed = posts
            .Feed.Where(w =>
                !listMemberDids.Contains(w.Post.Author.Did, new ATDidComparer())
                || mutualsDids.Contains(w.Post.Author.Did, new ATDidComparer())
            )
            .Select(s => new SkeletonFeedPost(s.Post.Uri.ToString()));

        return new SkeletonFeed(filteredFeed.ToArray(), posts.Cursor);
    }

    private async Task<IEnumerable<ATDid>> UpdateMutuals(
        CancellationToken cancellationToken = default
    )
    {
        var getResult = () => _followingList.Intersect(_followersList, new ATDidComparer());

        if ((DateTime.Now - _lastMutualsUpdate) < TimeSpan.FromMinutes(10))
        {
            _logger.LogDebug("Not updating mutuals list, too soon");
            return getResult();
        }

        if (_proto.Session == null)
        {
            _logger.LogWarning("Not logged in!");
            return getResult();
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return getResult();
        }

        _logger.LogDebug("Fetching follows list");
        var following = await BskyExtensions.GetAllResults(
            async (cursor, ct) =>
            {
                var r = await _proto.Graph.GetFollowsAsync(
                    _proto.Session.Handle,
                    cursor: cursor,
                    cancellationToken: ct
                );
                var d = r.HandleResult();
                return (d?.Follows, d?.Cursor);
            },
            cancellationToken
        );
        _followingList = following.Select(s => s.Did).ToList();

        if (cancellationToken.IsCancellationRequested)
        {
            return getResult();
        }

        _logger.LogDebug("Fetching followers list");
        var followers = await BskyExtensions.GetAllResults(
            async (cursor, ct) =>
            {
                var r = await _proto.Graph.GetFollowersAsync(
                    _proto.Session.Handle,
                    cursor: cursor,
                    cancellationToken: ct
                );
                var d = r.HandleResult();
                return (d?.Followers, d?.Cursor);
            },
            cancellationToken
        );
        _followersList = followers.Select(s => s.Did).ToList();

        _lastMutualsUpdate = DateTime.Now;
        return getResult();
    }

    private async Task<IEnumerable<ATDid>> UpdateListMembers(
        CancellationToken cancellationToken = default
    )
    {
        if ((DateTime.Now - _lastListMemberUpdate) < TimeSpan.FromMinutes(10))
        {
            _logger.LogDebug("Not updating list members, too soon");
            return _listMembers;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return _listMembers;
        }

        _logger.LogDebug("Fetching list members list");
        var listMembers = await BskyExtensions.GetAllResults(
            async (cursor, ct) =>
            {
                var r = await _proto.Graph.GetListAsync(
                    new ATUri(_feedConfig.ListUri),
                    cursor: cursor,
                    cancellationToken: ct
                );
                var d = r.HandleResult();
                return (d?.Items, d?.Cursor);
            },
            cancellationToken
        );

        var listMemberDids = listMembers
            .Select(s => s.Subject.Did)
            .Where(w => w != null)
            .Cast<ATDid>()
            .ToList();

        _listMembers = listMemberDids;
        _lastListMemberUpdate = DateTime.Now;

        return _listMembers;
    }
}
