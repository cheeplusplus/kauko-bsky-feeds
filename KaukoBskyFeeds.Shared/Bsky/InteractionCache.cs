using System.Collections.Concurrent;
using FishyFlip.Models;
using StackExchange.Redis;

namespace KaukoBskyFeeds.Shared.Bsky;

public interface IInteractionCache
{
    public Task RecordInteraction(
        ATUri feedUri,
        ATDid userDid,
        ATUri itemUri,
        string interaction
    );

    public Task<Dictionary<string, List<string>>> GetInteractions(ATUri feedUri, ATDid userDid);
}

public class RedisInteractionCache(IConnectionMultiplexer redis) : IInteractionCache
{
    private static readonly double ExptimeSeconds = TimeSpan.FromDays(3).TotalSeconds;
    private readonly IDatabaseAsync _redis = redis.GetDatabase();

    public async Task RecordInteraction(
        ATUri feedUri,
        ATDid userDid,
        ATUri itemUri,
        string interaction
    )
    {
        var key = GetKey(feedUri, userDid);
        var subkey = GetSubkey(itemUri, interaction);

        // This only records the latest interaction, which is fine for now since we're only going to see more/less
        // In the future if we ever see more types we'll need to record them and come up with a better data structure
        await _redis.HashSetAsync(key, subkey, DateTime.UtcNow.ToString("o"));
        await _redis.ExecuteAsync("HEXPIRE", key, ExptimeSeconds, "FIELDS", "1", subkey);
    }

    public async Task<Dictionary<string, List<string>>> GetInteractions(ATUri feedUri, ATDid userDid)
    {
        var hash = await _redis.HashGetAllAsync(GetKey(feedUri, userDid));
        var grouped = hash
            .Where(s => s.Name.ToString().Contains("::"))
            .GroupBy(
                g => g.Name.ToString().Split("::").First(),
                g => g.Name.ToString().Split("::").Last()
            );
        return grouped.ToDictionary(k => k.Key, v => v.ToList());
    }

    private string GetKey(ATUri feedUri, ATDid userDid) => $"feed_interaction_{feedUri}_{userDid}";
    private string GetSubkey(ATUri itemUri, string interaction) => $"{itemUri}::{interaction}";
}

public class MemoryInteractionCache : IInteractionCache
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<string>?>> _cache =
        new();

    public Task RecordInteraction(
        ATUri feedUri,
        ATDid userDid,
        ATUri itemUri,
        string interaction
    )
    {
        var key = GetKey(feedUri, userDid);

        _cache.AddOrUpdate(
            key,
            k =>
            {
                var newInnerDict = new ConcurrentDictionary<string, List<string>?>();
                newInnerDict.TryAdd(itemUri.ToString(), [interaction]);
                return newInnerDict;
            },
            (k, v) =>
            {
                v.AddOrUpdate(
                    itemUri.ToString(),
                    ik => [interaction],
                    (ik, iv) =>
                    {
                        var result = iv ?? [];
                        result.Add(interaction);
                        return result;
                    }
                );
                return v;
            }
        );

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
}