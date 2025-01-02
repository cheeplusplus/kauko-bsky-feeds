using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models;

/// <summary>
/// Top level JSON message from Jetstream
/// </summary>
public class JetstreamMessage
{
    [JsonPropertyName("did")]
    public required string Did { get; set; }

    [JsonPropertyName("time_us")]
    public long TimeMicroseconds { get; set; }

    [JsonPropertyName("kind")]
    public required string Kind { get; set; }

    // This should probably be a discriminator on 'kind' but all three types are just keyed (commit/identity/account)
    [JsonPropertyName("commit")]
    public JetstreamCommit? Commit { get; set; }

    [JsonIgnore]
    public DateTime MessageTime => BskyExtensions.FromMicroseconds(TimeMicroseconds);

    public string ToAtUri()
    {
        var path = string.Join('/', [Commit?.Collection, Commit?.RecordKey]);
        var pathStr = string.IsNullOrEmpty(path) ? "" : $"/{path}";
        return $"at://{Did}{pathStr}";
    }
}
