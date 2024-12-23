using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public class AppBskyFeedRepost : JetstreamRecord
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("subject")]
    public required AtStrongRef Subject { get; set; }
}
