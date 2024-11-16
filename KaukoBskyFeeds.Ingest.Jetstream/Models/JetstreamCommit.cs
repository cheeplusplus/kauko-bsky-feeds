using System.Text.Json.Serialization;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models;

public class JetstreamCommit
{
    [JsonPropertyName("rev")]
    public required string Revision { get; set; }

    [JsonPropertyName("operation")]
    public required string OperationString { get; set; }

    [JsonPropertyName("collection")]
    public required string Collection { get; set; }

    [JsonPropertyName("rkey")]
    public required string RecordKey { get; set; }

    [JsonPropertyName("record")]
    public JetstreamRecord? Record { get; set; } = default!;

    [JsonPropertyName("cid")]
    public string? Cid { get; set; }

    // Helpers
    [JsonIgnore]
    public JetstreamOperation Operation =>
        (
            OperationString switch
            {
                "create" => JetstreamOperation.Create,
                "delete" => JetstreamOperation.Delete,
                "update" => JetstreamOperation.Update,
                _ => JetstreamOperation.Unknown,
            }
        );
}

public enum JetstreamOperation
{
    Unknown,
    Create,
    Delete,
    Update,
}
