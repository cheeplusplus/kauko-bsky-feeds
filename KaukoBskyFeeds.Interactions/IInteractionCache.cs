using FishyFlip.Models;

namespace KaukoBskyFeeds.Interactions;

public interface IInteractionCache
{
    public Task RecordInteraction(FeedInteractionEvent feedEvent);
    public Task RecordInteractions(IEnumerable<FeedInteractionEvent> feedEvents);

    public Task<Dictionary<string, List<string>>> GetInteractions(ATUri feedUri, ATDid userDid);
}
