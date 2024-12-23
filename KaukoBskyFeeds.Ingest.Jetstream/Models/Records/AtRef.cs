using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public class AtRef
{
    [JsonPropertyName("$link")]
    public required string Link { get; set; }
}
