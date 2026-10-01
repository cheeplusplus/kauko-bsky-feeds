using EFCore.BulkExtensions;
using FishyFlip.Models;
using KaukoBskyFeeds.Db;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Interactions;

// Cleanup is currently handled with everything else in JetstreamWorker

public class DbInteractionCache(FeedDbContext dbContext) : IInteractionCache
{
    public async Task RecordInteraction(FeedInteractionEvent feedEvent)
    {
        await dbContext.BulkInsertOrUpdateAsync([feedEvent.ToDb()]);
    }

    public async Task RecordInteractions(IEnumerable<FeedInteractionEvent> feedEvents)
    {
        await dbContext.BulkInsertOrUpdateAsync(feedEvents.Select(s => s.ToDb()));
    }

    public async Task<Dictionary<string, List<string>>> GetInteractions(
        ATUri feedUri,
        ATDid userDid
    )
    {
        return await dbContext
            .FeedInteractions.Where(f =>
                f.FeedUri == feedUri.ToString() && f.UserDid == userDid.ToString()
            )
            .GroupBy(g => g.ItemUri)
            .ToDictionaryAsync(k => k.Key, v => v.Select(s => s.Interaction).ToList());
    }
}
