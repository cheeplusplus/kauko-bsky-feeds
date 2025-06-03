using System.Collections.Concurrent;
using StackExchange.Redis;

namespace KaukoBskyFeeds.Shared.Redis;

public interface IFastCounter
{
    Task Increment(string key, string hash, int amount, TimeSpan expiry, ExpireWhen expireWhen = ExpireWhen.Always);
    Task Delete(string key);
    Task<int> Get(string key, string hash);
    Task<Dictionary<string, int>> GetAll(string key);
    Task<int> GetAllSum(string key);
}

public class FastCounterRedis(IDatabaseAsync db) : IFastCounter
{
    public async Task Increment(string key, string hash, int amount, TimeSpan expiry, ExpireWhen expireWhen = ExpireWhen.Always)
    {
        if (amount == 0)
            return;

        await db.HashIncrementAsync(key, hash, amount, CommandFlags.FireAndForget);
        await db.KeyExpireAsync(key, expiry, expireWhen, CommandFlags.FireAndForget);
    }

    public async Task Delete(string key)
    {
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
    }

    public async Task<int> Get(string key, string hash)
    {
        var value = await db.HashGetAsync(key, hash);

        if (value.TryParse(out int res))
        {
            return res;
        }

        return 0;
    }

    public async Task<Dictionary<string, int>> GetAll(string key)
    {
        var values = await db.HashGetAllAsync(key);
        return values.ToDictionary(k => (string)k.Name!, v => v.Value.IsInteger ? (int)v.Value : 0);
    }

    public async Task<int> GetAllSum(string key)
    {
        var values = await db.HashGetAllAsync(key);
        return values.Sum(k => k.Value.IsInteger ? (int)k.Value : 0);
    }
}

public class FastCounterMemory : IFastCounter
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _storage =
        new();

    public Task Increment(string key, string hash, int amount, TimeSpan expiry, ExpireWhen expireWhen)
    {
        _storage.AddOrUpdate(
            key,
            new ConcurrentDictionary<string, int>([new(hash, amount)]),
            (k, v) =>
            {
                v.AddOrUpdate(hash, amount, (ik, iv) => iv + amount);
                return v;
            }
        );
        return Task.CompletedTask;
    }

    public Task Delete(string key)
    {
        _storage.Remove(key, out _);
        return Task.CompletedTask;
    }

    public Task<int> Get(string key, string hash)
    {
        var keyVal = _storage.GetValueOrDefault(key);
        if (keyVal == null)
        {
            return Task.FromResult(0);
        }

        var hashVal = keyVal.GetValueOrDefault(hash, 0);
        return Task.FromResult(hashVal);
    }

    public Task<Dictionary<string, int>> GetAll(string key)
    {
        var keyVal = _storage.GetValueOrDefault(key);
        return Task.FromResult(
            keyVal == null
                ? new Dictionary<string, int>()
                : keyVal.ToDictionary(k => k.Key, v => v.Value)
        );
    }

    public async Task<int> GetAllSum(string key)
    {
        var values = await GetAll(key);
        return values.Sum(k => k.Value);
    }
}

public static class FastCounterKeys
{
    public static string Post(string did, string rkey) => $"fastcounter:post:{{{did}}}:{rkey}";

    public const string Like = "like";
    public const string Repost = "repost";
    public const string Reply = "reply";
    public const string Quote = "quote";
}
