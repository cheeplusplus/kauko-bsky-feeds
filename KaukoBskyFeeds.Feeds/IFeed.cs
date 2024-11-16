using FishyFlip.Models;
using KaukoBskyFeeds.Shared.Bsky.Models;

namespace KaukoBskyFeeds.Feeds;

public interface IFeed
{
    public string DisplayName { get; }
    public string Description { get; }

    Task<CustomSkeletonFeed> GetFeedSkeleton(
        ATDid? requestor,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    );
}
