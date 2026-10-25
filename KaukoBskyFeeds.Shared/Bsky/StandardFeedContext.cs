using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Shared.Bsky;

public class StandardFeedContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    [JsonPropertyName("feed")]
    public required string FeedUri { get; set; }

    [JsonPropertyName("ctx")]
    public string? ProvidedCtx { get; set; }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static StandardFeedContext? Deserialize(string? json)
    {
        if (json == null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<StandardFeedContext>(json, JsonOptions);
    }
}
