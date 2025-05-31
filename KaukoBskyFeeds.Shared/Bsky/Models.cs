using FishyFlip.Lexicon.App.Bsky.Feed;

namespace KaukoBskyFeeds.Shared.Bsky;

public record CustomSkeletonFeed(IEnumerable<SkeletonFeedPost> Feed, string? Cursor);
