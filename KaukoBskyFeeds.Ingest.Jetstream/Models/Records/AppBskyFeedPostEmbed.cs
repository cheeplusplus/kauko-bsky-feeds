using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedImages), BskyConstants.PostEmbedTypeImages)]
[JsonDerivedType(typeof(AppBskyFeedPostEmbedRecord), BskyConstants.PostEmbedTypeRecord)]
[JsonDerivedType(
    typeof(AppBskyFeedPostEmbedRecordWithMedia),
    BskyConstants.PostEmbedTypeRecordWithMedia
)]
public class AppBskyFeedPostEmbed
{
    public string GetRecordType()
    {
        return this switch
        {
            AppBskyFeedPostEmbedImages => BskyConstants.PostEmbedTypeImages,
            AppBskyFeedPostEmbedRecord => BskyConstants.PostEmbedTypeRecord,
            AppBskyFeedPostEmbedRecordWithMedia => BskyConstants.PostEmbedTypeRecordWithMedia,
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
