using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record ListImagesOnlyFeedConfig(
    string DisplayName,
    string Description,
    string ListUri,
    bool Install = false,
    bool RestrictToFeedOwner = false,
    bool FetchTimeline = false,
    List<string>? AlwaysShowListUser = null,
    bool ShowSelfPosts = true
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);
