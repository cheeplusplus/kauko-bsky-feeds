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
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowRepliesSetting>))]
        ShowRepostsSetting ShowReposts = ShowRepostsSetting.All,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowRepliesSetting>))]
        ShowRepliesSetting ShowReplies = ShowRepliesSetting.All,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowQuotePostsSetting>))]
        ShowQuotePostsSetting ShowQuotePosts = ShowQuotePostsSetting.All,
    bool IncludeListMutuals = false
) : BaseFeedConfig(DisplayName, Description);

public enum ShowRepostsSetting
{
    All,
    None,
    FollowingOnly,
}

public enum ShowRepliesSetting
{
    All,
    None,
    FollowingOnly,
    FollowingOnlyTail,
}

public enum ShowQuotePostsSetting
{
    All,
    None,
    FollowingOnly,
}
