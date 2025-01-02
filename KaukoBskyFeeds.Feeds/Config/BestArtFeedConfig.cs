using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record BestArtFeedConfig(
    string DisplayName,
    string Description,
    bool Install = false,
    bool RestrictToFeedOwner = false,
    bool? BalanceInteractions = false,
    string? RestrictToListUri = null
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);
