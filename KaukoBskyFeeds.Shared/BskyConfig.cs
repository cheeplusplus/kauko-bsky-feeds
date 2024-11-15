using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Shared;

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
    List<string>? MuteUsers = null,
    bool ShowSelfPosts = true,
    bool ShowReposts = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowRepliesSetting>))]
        ShowRepliesSetting ShowReplies = ShowRepliesSetting.All,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowQuotePostsSetting>))]
        ShowQuotePostsSetting ShowQuotePosts = ShowQuotePostsSetting.All,
    bool IncludeListMutuals = false
) : BaseFeedConfig(DisplayName, Description);

public enum ShowRepliesSetting
{
    [EnumMember(Value = "all")]
    All,

    [EnumMember(Value = "none")]
    None,

    [EnumMember(Value = "following-only")]
    FollowingOnly,

    [EnumMember(Value = "following-only-tail")]
    FollowingOnlyTail,
}

public enum ShowQuotePostsSetting
{
    [EnumMember(Value = "all")]
    All,

    [EnumMember(Value = "none")]
    None,

    [EnumMember(Value = "following-only")]
    FollowingOnly,
}
