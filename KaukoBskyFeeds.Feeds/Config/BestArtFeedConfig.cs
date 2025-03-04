using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record BestArtFeedConfig(
    string DisplayName,
    string Description,
    int FeedLimit = 25,
    int MinInteractions = 3,
    bool Install = false,
    bool RestrictToFeedOwner = false,
    bool? BalanceInteractions = false,
    string? RestrictToListUri = null
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);
