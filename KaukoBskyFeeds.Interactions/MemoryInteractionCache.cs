using System.Collections.Concurrent;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Interactions;

public class MemoryInteractionCache : IInteractionCache
{
    private readonly ConcurrentDictionary<
        string,
        ConcurrentDictionary<string, List<string>?>
    > _cache = new();

    public Task RecordInteraction(FeedInteractionEvent feedEvent)
    {
        var key = GetKey(feedEvent);

        _cache.AddOrUpdate(
            key,
            k =>
            {
                var newInnerDict = new ConcurrentDictionary<string, List<string>?>();
                newInnerDict.TryAdd(feedEvent.ItemUri.ToString(), [feedEvent.Interaction]);
                return newInnerDict;
            },
            (k, v) =>
            {
                v.AddOrUpdate(
                    feedEvent.ItemUri.ToString(),
                    ik => [feedEvent.Interaction],
                    (ik, iv) =>
                    {
                        var result = iv ?? [];
                        result.Add(feedEvent.Interaction);
                        return result;
                    }
                );
                return v;
            }
        );

        return Task.CompletedTask;
    }

    public Task RecordInteractions(IEnumerable<FeedInteractionEvent> feedEvents)
    {
        foreach (var feedEvent in feedEvents)
        {
            RecordInteraction(feedEvent);
        }

        return Task.CompletedTask;
    }

    public Task<Dictionary<string, List<string>>> GetInteractions(ATUri feedUri, ATDid userDid)
    {
        var key = GetKey(feedUri, userDid);
        var top = _cache.GetValueOrDefault(key);
        return Task.FromResult(
            top != null
                ? top.ToDictionary(s => s.Key, s => s.Value ?? [])
                : new Dictionary<string, List<string>>()
        );
    }

    private string GetKey(ATUri feedUri, ATDid userDid) => $"{feedUri}_{userDid}";

    private string GetKey(FeedInteractionEvent feedEvent) =>
        GetKey(feedEvent.FeedUri, feedEvent.UserDid);
}
