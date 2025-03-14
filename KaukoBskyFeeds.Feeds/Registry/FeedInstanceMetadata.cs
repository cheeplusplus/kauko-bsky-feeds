using FishyFlip.Models;

namespace KaukoBskyFeeds.Feeds.Registry;

/// <summary>
/// Information about a live feed instance.
/// </summary>
/// <param name="FeedUri">Feed URI</param>
public record FeedInstanceMetadata(ATUri FeedUri);
