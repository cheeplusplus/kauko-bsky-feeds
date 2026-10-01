using FishyFlip.Models;
using StackExchange.Redis;

namespace KaukoBskyFeeds.Interactions;

// Needs Redis 7.4 for HEXPIRE
public class RedisInteractionCache(IConnectionMultiplexer redis) : IInteractionCache
{
    private static readonly double ExptimeSeconds = TimeSpan.FromDays(3).TotalSeconds;
    private readonly IDatabaseAsync _redis = redis.GetDatabase();

    public async Task RecordInteraction(FeedInteractionEvent feedEvent)
    {
        var (feedUri, userDid, itemUri, interaction) = feedEvent;
        var key = GetKey(feedUri, userDid);
        var subkey = GetSubkey(itemUri, interaction);

        await _redis.HashSetAsync(key, subkey, DateTime.UtcNow.ToString("o"));
        await _redis.ExecuteAsync("HEXPIRE", key, ExptimeSeconds, "FIELDS", "1", subkey);
    }

    public async Task RecordInteractions(IEnumerable<FeedInteractionEvent> feedEvents)
    {
        foreach (var feedEvent in feedEvents)
        {
            await RecordInteraction(feedEvent);
        }
    }

    public async Task<Dictionary<string, List<string>>> GetInteractions(
        ATUri feedUri,
        ATDid userDid
    )
    {
        var hash = await _redis.HashGetAllAsync(GetKey(feedUri, userDid));
        var grouped = hash.Where(s => s.Name.ToString().Contains("::"))
            .GroupBy(
                g => g.Name.ToString().Split("::").First(),
                g => g.Name.ToString().Split("::").Last()
            );
        return grouped.ToDictionary(k => k.Key, v => v.ToList());
    }

    private string GetKey(ATUri feedUri, ATDid userDid) => $"feed_interaction_{feedUri}_{userDid}";

    private string GetSubkey(ATUri itemUri, string interaction) => $"{itemUri}::{interaction}";
}
