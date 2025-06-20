using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record TimelineAdvancedFeedConfig(
    string DisplayName,
    string Description,
    bool Install = false,
    bool RestrictToFeedOwner = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<DataOriginSetting>))]
        DataOriginSetting DataOrigin = DataOriginSetting.Ingest,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TimelineStyleSetting>))]
        TimelineStyleSetting TimelineStyle = TimelineStyleSetting.Following,
    string? SourceList = null,
    List<string>? SubtractLists = null,
    List<string>? AlwaysShowUser = null,
    List<string>? AlwaysShowUserReposts = null,
    List<string>? MuteUsers = null,
    bool ShowSelfPosts = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowRepliesSetting>))]
        ShowRepostsSetting ShowReposts = ShowRepostsSetting.All,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowRepliesSetting>))]
        ShowRepliesSetting ShowReplies = ShowRepliesSetting.All,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowQuotePostsSetting>))]
        ShowQuotePostsSetting ShowQuotePosts = ShowQuotePostsSetting.All,
    bool IncludeListMutuals = false
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);

public enum DataOriginSetting
{
    Api,
    Ingest,
}

public enum TimelineStyleSetting
{
    Following,
    List,
}

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
