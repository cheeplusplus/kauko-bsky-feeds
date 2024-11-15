using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPost), BskyConstants.COLLECTION_TYPE_POST)]
public abstract class JetstreamRecord
{
    [JsonPropertyName("$type")]
    public string Type { get; set; } = null!;
}
