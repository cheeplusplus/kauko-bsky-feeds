using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record TimelineArtOnlyFeedConfig(
    string DisplayName,
    string Description,
    string ListUri,
    List<string>? AlwaysShowListUser = null,
    bool ShowSelfPosts = true
) : BaseFeedConfig(DisplayName, Description);
