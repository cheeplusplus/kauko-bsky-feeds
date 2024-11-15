using System.Text.Json.Serialization;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public class AtStrongRef
{
    [JsonPropertyName("cid")]
    public string Cid { get; set; } = null!;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = null!;
}
