using FishyFlip.Models;

namespace KaukoBskyFeeds.FeedProcessor.Feeds;

public interface IFeed
{
    public string DisplayName { get; }
    public string Description { get; }

    Task<SkeletonFeed> GetFeedSkeleton(
        int? limit,
        string? cursor,
        CancellationToken cancellationToken = default
    );
}
