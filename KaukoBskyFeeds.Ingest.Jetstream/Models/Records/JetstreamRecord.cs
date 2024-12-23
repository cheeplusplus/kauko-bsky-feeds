using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPost), BskyConstants.COLLECTION_TYPE_POST)]
[JsonDerivedType(typeof(AppBskyFeedLike), BskyConstants.COLLECTION_TYPE_LIKE)]
[JsonDerivedType(typeof(AppBskyFeedRepost), BskyConstants.COLLECTION_TYPE_REPOST)]
public class JetstreamRecord { }
