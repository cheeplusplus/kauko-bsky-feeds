using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Feeds;

public class TimelineArtOnly : IFeed
{
    private readonly ILogger<TimelineArtOnly> _logger;
    private readonly ATProtocol _proto;
    private readonly TimelineArtOnlyFeedConfig _feedConfig;
    private readonly FeedDbContext _db;
    private readonly IBskyCache _cache;

    public TimelineArtOnly(
        ILogger<TimelineArtOnly> logger,
        ATProtocol proto,
        TimelineArtOnlyFeedConfig feedConfig,
        FeedDbContext db,
        IBskyCache cache
    )
    {
        _logger = logger;
        _proto = proto;
        _feedConfig = feedConfig;
        _db = db;
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

        var listMemberDids = await _cache.GetListMembers(
            _proto,
            new ATUri(_feedConfig.ListUri),
            cancellationToken
        );

        // While it's supposed to be opaque, the Bsky timeline cursor is an ISO-8601 string
        // Obeying this lets it walk backwards
        DateTime.TryParse(
            cursor,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var cursorAsDate
        );

        var posts = await _db
            .Posts.Where(w =>
                listMemberDids.Contains(new ATDid(w.Did), new ATDidComparer())
                && w.Embeds != null
                && w.Embeds.Images != null
                && w.Embeds.Images.Count > 0
                && w.ReplyParentUri == null // TODO: allow self-replies by comparing the DID with the post's
                && (cursorAsDate == default || w.EventTime > cursorAsDate)
            )
            .OrderByDescending(o => o.EventTime)
            .Take(limit ?? 50)
            .ToListAsync(cancellationToken);
        var filteredFeed = posts.Select(s => new SkeletonFeedPost(s.ToUri(), null));
        var newCursor = posts.FirstOrDefault()?.EventTime.ToString("o");

        return new SkeletonFeed(filteredFeed.ToArray(), newCursor);
    }
}
