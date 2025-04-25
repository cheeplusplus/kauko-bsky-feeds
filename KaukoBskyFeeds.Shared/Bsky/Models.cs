using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Shared.Bsky;

public record CustomSkeletonFeed(IEnumerable<SkeletonFeedPost> Feed, string? Cursor);
