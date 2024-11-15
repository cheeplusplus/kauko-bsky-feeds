using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedImages), BskyConstants.POST_EMBED_TYPE_IMAGES)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedRecord), BskyConstants.POST_EMBED_TYPE_RECORD)]
public class AppBskyFeedPostEmbed
{
    [JsonPropertyName("$type")]
    public string Type { get; set; } = null!;
}

public class AppBskyFeedPostEmbedImages : AppBskyFeedPostEmbed
{
    [JsonPropertyName("images")]
    public List<AppBskyFeedPostEmbedImage> Images { get; set; } = null!;
}

public class AppBskyFeedPostEmbedImage
{
    [JsonPropertyName("image")]
    public AppBskyFeedPostEmbedImageImage Image { get; set; } = null!;
}

public class AppBskyFeedPostEmbedImageImage
{
    [JsonPropertyName("$type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("ref")]
    public AtRef? Ref { get; set; } = null!;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = null!;
}

public class AppBskyFeedPostEmbedRecord : AppBskyFeedPostEmbed
{
    [JsonPropertyName("record")]
    public AtStrongRef Record { get; set; } = null!;
}
