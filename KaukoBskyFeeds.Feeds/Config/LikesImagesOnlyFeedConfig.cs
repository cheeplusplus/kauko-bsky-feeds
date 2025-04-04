using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record LikesImagesOnlyFeedConfig(
    string DisplayName,
    string Description,
    bool Install = false,
    bool RestrictToFeedOwner = false,
    bool FetchTimeline = false
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);
