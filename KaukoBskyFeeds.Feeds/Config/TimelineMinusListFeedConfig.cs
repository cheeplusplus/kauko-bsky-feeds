using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

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
