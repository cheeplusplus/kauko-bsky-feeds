using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public class AtRef
{
    [JsonPropertyName("$link")]
    public string Link { get; set; } = null!;
}
