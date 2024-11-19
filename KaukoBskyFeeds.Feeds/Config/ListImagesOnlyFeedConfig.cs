using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record ListImagesOnlyFeedConfig(
    string DisplayName,
    string Description,
    string ListUri,
    List<string>? AlwaysShowListUser = null,
    bool ShowSelfPosts = true
) : BaseFeedConfig(DisplayName, Description);
