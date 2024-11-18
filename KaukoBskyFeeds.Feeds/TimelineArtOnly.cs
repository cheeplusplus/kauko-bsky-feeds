using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Feeds;

[BskyFeed(nameof(TimelineArtOnly), typeof(TimelineArtOnlyFeedConfig))]
public class TimelineArtOnly(
    ILogger<TimelineArtOnly> logger,
    ATProtocol proto,
    TimelineArtOnlyFeedConfig feedConfig,
    FeedDbContext db,
    IBskyCache cache
) : IFeed
{
    public string DisplayName => feedConfig.DisplayName;

    public string Description => feedConfig.Description;

    public async Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogDebug("Fetching timeline");
        var listMemberDids = await cache.GetListMembers(
            proto,
            new ATUri(feedConfig.ListUri),
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

        var feedDids = listMemberDids.Select(s => s.Handler);
        var posts = await db
            .Posts.OrderByDescending(o => o.EventTime)
            .Where(w =>
                feedDids.Contains(w.Did)
                && w.Embeds != null
                && w.Embeds.Images != null
                && w.Embeds.Images.Count > 0
                && w.ReplyParentUri == null // TODO: allow self-replies by comparing the DID with the post's
                && (cursorAsDate == default || w.EventTime > cursorAsDate)
            )
            .OrderByDescending(o => o.EventTime)
            .Take(limit ?? 50)
            .ToListAsync(cancellationToken);
        var filteredFeed = posts.Select(s => new CustomSkeletonFeedPost(s.ToUri(), null));
        var newCursor = posts.FirstOrDefault()?.EventTime.ToString("o");

        return new CustomSkeletonFeed(filteredFeed.ToList(), newCursor);
    }
}
