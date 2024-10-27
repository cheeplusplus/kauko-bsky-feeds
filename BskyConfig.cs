namespace KaukoBskyFeeds;

public record BskyConfigBlock(
    BskyConfigAuth Auth,
    BskyConfigIdentity Identity,
    Dictionary<string, BskyConfigFeedProcessor> FeedProcessors,
    bool EnableInstall = false
);

public record BskyConfigAuth(string Username, string Password);

public record BskyConfigIdentity(string Hostname, string PublishedAtUri)
{
    public string ServiceDid => $"did:web:{Hostname}";
    public string BskyFeedGeneratorServiceEndpoint => $"https://{Hostname}";
}

public record BskyConfigFeedProcessor(string Type, BaseFeedConfig Config);

public record BaseFeedConfig(string DisplayName, string Description);

public record TimelineMinusListFeedConfig(
    string DisplayName,
    string Description,
    string ListUri,
    List<string>? AlwaysShowListUser = null,
    bool ShowSelfPosts = true
) : BaseFeedConfig(DisplayName, Description);
