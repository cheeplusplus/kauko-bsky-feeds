using System.Runtime.Serialization;
using FishyFlip.Lexicon.App.Bsky.Feed;

namespace KaukoBskyFeeds.Shared.Bsky;

public record CustomSkeletonFeed(IEnumerable<SkeletonFeedPost> Feed, string? Cursor);

public class CustomSendInteractions
{
    public required List<CustomFeedInteraction> Interactions { get; set; }
    public string? Feed { get; set; }

    public class CustomFeedInteraction
    {
        public string? Event { get; set; }
        public string? FeedContext { get; set; }
        public string? Item { get; set; }
        public string? ReqId { get; set; }
    }
}
