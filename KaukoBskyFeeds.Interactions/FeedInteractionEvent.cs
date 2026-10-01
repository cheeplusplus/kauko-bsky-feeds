using FishyFlip.Models;
using KaukoBskyFeeds.Db.Models;

namespace KaukoBskyFeeds.Interactions;

public record FeedInteractionEvent(ATUri FeedUri, ATDid UserDid, ATUri ItemUri, string Interaction)
{
    public FeedInteraction ToDb()
    {
        return new FeedInteraction
        {
            EventTime = DateTime.UtcNow,
            FeedUri = FeedUri.ToString(),
            UserDid = UserDid.ToString(),
            ItemUri = ItemUri.ToString(),
            Interaction = Interaction,
        };
    }
}
