using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record RepostsOnlyFeedConfig(
    string DisplayName,
    string Description,
    List<string>? OnlyShowUsers = null,
    bool ExcludeSelfReposts = false,
    // bool RequireImages = false, // not available without consuming all posts
    bool Install = false,
    bool RestrictToFeedOwner = false,
    bool FetchTimeline = false
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);
