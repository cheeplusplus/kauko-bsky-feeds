using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared;

namespace KaukoBskyFeeds.Feeds.Config;

public record PopularAmongFeedConfig(
    string DisplayName,
    string Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PopularAmongGroupSetting>))]
        PopularAmongGroupSetting TargetGroup = PopularAmongGroupSetting.Following,
    int MinRelevance = 2,
    bool ImagesOnly = false,
    bool ImagesChecksServer = false,
    bool Install = false,
    bool RestrictToFeedOwner = false
) : BaseFeedConfig(DisplayName, Description, RestrictToFeedOwner, Install);

public enum PopularAmongGroupSetting
{
    Followers,
    Following,
    Mutuals,
}
