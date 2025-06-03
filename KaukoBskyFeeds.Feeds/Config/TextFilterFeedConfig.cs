using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record TextFilterFeedConfig(
    string DisplayName,
    string Description,
    Dictionary<string, List<TextFilterFilterOperation>> FilterOperations,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShowQuotePostsSetting>))]
    FeedSourceSetting FeedSource = FeedSourceSetting.All,
    string? FeedSourceList = null,
    bool Install = false,
    bool RestrictToFeedOwner = false
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);

public enum FeedSourceSetting
{
    All,
    Following,
    List,
}

public record TextFilterFilterOperation(string? Contains = null, Regex? Expression = null, bool Exclude = false);