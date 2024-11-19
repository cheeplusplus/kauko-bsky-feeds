using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky.Models;

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
