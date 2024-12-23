using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public class AtStrongRef
{
    [JsonPropertyName("cid")]
    public required string Cid { get; set; }

    [JsonPropertyName("uri")]
    public required string Uri { get; set; }
}
