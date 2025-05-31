using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record ListImagesOnlyFeedConfig(
    string DisplayName,
    string Description,
    string ListUri,
    bool Install = false,
    bool RestrictToFeedOwner = false,
    bool FetchTimeline = false
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);
