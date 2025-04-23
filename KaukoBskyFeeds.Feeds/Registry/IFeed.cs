using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Feeds.Registry;

public interface IFeed
{
    Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    );

    BaseFeedConfig Config { get; }
}
