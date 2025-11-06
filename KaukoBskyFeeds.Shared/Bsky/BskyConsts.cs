namespace KaukoBskyFeeds.Shared.Bsky;

public static class BskyConstants
{
    public const string XrpcGetFeedSkeleton = "app.bsky.feed.getFeedSkeleton";
    public const string XrpcSendInteractions = "app.bsky.feed.sendInteractions";

    public const string CollectionTypeFeedGenerator = "app.bsky.feed.generator";
    public const string CollectionTypePost = "app.bsky.feed.post";
    public const string CollectionTypeLike = "app.bsky.feed.like";
    public const string CollectionTypeRepost = "app.bsky.feed.repost";

    public const string PostEmbedTypeImages = "app.bsky.embed.images";
    public const string PostEmbedTypeVideo = "app.bsky.embed.video";
    public const string PostEmbedTypeExternal = "app.bsky.embed.external";
    public const string PostEmbedTypeRecord = "app.bsky.embed.record";
    public const string PostEmbedTypeRecordWithMedia = "app.bsky.embed.recordWithMedia";
    
    public const string InteractionSeen = "app.bsky.feed.defs#interactionSeen";
    public const string InteractionRequestLess = "app.bsky.feed.defs#requestLess";
    public const string InteractionRequestMore = "app.bsky.feed.defs#requestMore";
}
