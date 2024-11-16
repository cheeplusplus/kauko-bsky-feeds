using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedImages), BskyConstants.POST_EMBED_TYPE_IMAGES)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedRecord), BskyConstants.POST_EMBED_TYPE_RECORD)]
public class AppBskyFeedPostEmbed
{
    [JsonPropertyName("$type")]
    public required string Type { get; set; }
}

public class AppBskyFeedPostEmbedImages : AppBskyFeedPostEmbed
{
    [JsonPropertyName("images")]
    public required List<AppBskyFeedPostEmbedImage> Images { get; set; }
}

public class AppBskyFeedPostEmbedImage
{
    [JsonPropertyName("image")]
    public required AppBskyFeedPostEmbedImageImage Image { get; set; }
}

public class AppBskyFeedPostEmbedImageImage
{
    [JsonPropertyName("$type")]
    public required string Type { get; set; }

    [JsonPropertyName("ref")]
    public AtRef? Ref { get; set; }

    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }
}

public class AppBskyFeedPostEmbedRecord : AppBskyFeedPostEmbed
{
    [JsonPropertyName("record")]
    public required AtStrongRef Record { get; set; }
}
