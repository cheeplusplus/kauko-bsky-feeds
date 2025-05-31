using System.Text.Json.Serialization;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AppBskyFeedPost), BskyConstants.CollectionTypePost)]
[JsonDerivedType(typeof(AppBskyFeedLike), BskyConstants.CollectionTypeLike)]
[JsonDerivedType(typeof(AppBskyFeedRepost), BskyConstants.CollectionTypeRepost)]
public class JetstreamRecord { }
