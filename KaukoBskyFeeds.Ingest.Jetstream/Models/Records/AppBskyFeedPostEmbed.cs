using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedImages), BskyConstants.POST_EMBED_TYPE_IMAGES)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedRecord), BskyConstants.POST_EMBED_TYPE_RECORD)]
[JsonDerivedType(
    typeof(AppBskyFeedPostEmbedRecordWithMedia),
    BskyConstants.POST_EMBED_TYPE_RECORD_WITH_MEDIA
)]
public class AppBskyFeedPostEmbed
{
    public string GetRecordType()
    {
        return this switch
        {
            AppBskyFeedPostEmbedImages ei => BskyConstants.POST_EMBED_TYPE_IMAGES,
            AppBskyFeedPostEmbedRecord er => BskyConstants.POST_EMBED_TYPE_RECORD,
            AppBskyFeedPostEmbedRecordWithMedia rwm =>
                BskyConstants.POST_EMBED_TYPE_RECORD_WITH_MEDIA,
            _ => "other",
        };
    }
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
    [JsonPropertyName("ref")]
    public AtRef? Ref { get; set; }

    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }
}

public interface IAppBskyFeedPostEmbedWithRecord
{
    AtStrongRef? Record { get; }
}

public class AppBskyFeedPostEmbedRecord : AppBskyFeedPostEmbed, IAppBskyFeedPostEmbedWithRecord
{
    [JsonPropertyName("record")]
    public required AtStrongRef Record { get; set; }
}

public class AppBskyFeedPostEmbedRecordWithMedia
    : AppBskyFeedPostEmbed,
        IAppBskyFeedPostEmbedWithRecord
{
    [JsonPropertyName("media")]
    public required AppBskyFeedPostEmbed Media { get; set; }

    [JsonPropertyName("record")]
    public required AppBskyFeedPostEmbed RawRecord { get; set; }

    [JsonIgnore]
    public AtStrongRef? Record
    {
        get
        {
            if (RawRecord is not AppBskyFeedPostEmbedRecord embedRecord)
            {
                return null;
            }
            return embedRecord.Record;
        }
    }
}
