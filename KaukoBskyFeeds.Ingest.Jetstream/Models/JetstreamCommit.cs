using System.Text.Json.Serialization;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models;

public class JetstreamCommit
{
    [JsonPropertyName("rev")]
    public string Revision { get; set; } = null!;

    [JsonPropertyName("operation")]
    public string OperationString { get; set; } = null!;

    [JsonPropertyName("collection")]
    public string Collection { get; set; } = null!;

    [JsonPropertyName("rkey")]
    public string RecordKey { get; set; } = null!;

    [JsonPropertyName("record")]
    public JetstreamRecord? Record { get; set; } = default!;

    [JsonPropertyName("cid")]
    public string Cid { get; set; } = null!;

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
