using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record BestArtFeedConfig(
    string DisplayName,
    string Description,
    bool? BalanceInteractions = false,
    string? RestrictToListUri = null
) : BaseFeedConfig(DisplayName, Description);
