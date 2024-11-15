using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public class AppBskyFeedPost : JetstreamRecord
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("langs")]
    public List<string>? Langs { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = null!;

    [JsonPropertyName("reply")]
    public AppBskyFeedReplyPost? Reply { get; set; }

    [JsonPropertyName("embed")]
    public AppBskyFeedPostEmbed? Embed { get; set; }
}

public class AppBskyFeedReplyPost
{
    [JsonPropertyName("parent")]
    public AtStrongRef Parent { get; set; } = default!;

    [JsonPropertyName("root")]
    public AtStrongRef Root { get; set; } = default!;
}
