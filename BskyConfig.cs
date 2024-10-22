namespace KaukoBskyFeeds;

public record BskyConfigBlock(
    BskyConfigAuth Auth,
    BskyConfigIdentity Identity,
    BskyConfigFeedProcessors FeedProcessors,
    bool EnableInstall = false
);

public record BskyConfigAuth(string Username, string Password);

public record BskyConfigIdentity(string Hostname, string PublishedAtUri)
{
    public string ServiceDid => $"did:web:{Hostname}";
    public string BskyFeedGeneratorServiceEndpoint => $"https://{Hostname}";
}

public class BskyConfigFeedProcessors
{
    public TimelineMinusListFeedConfig KaukoMinusArtists { get; set; } = default!;
}

public record BaseFeedConfig(string DisplayName, string Description);

public record TimelineMinusListFeedConfig(string DisplayName, string Description, string ListUri)
    : BaseFeedConfig(DisplayName, Description);
