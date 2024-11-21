using StackExchange.Redis;

namespace KaukoBskyFeeds.Redis.FastStore;

public class RedisFastStore(IDatabase redis) : IFastStore
{
    private const string STORE_LIKES = "likes";
    private const string STORE_REPLIES = "replies";
    private const string STORE_REPOSTS = "reposts";
    private const string STORE_QUOTE_POSTS = "quoteposts";

    public Task<long> GetLikeCount(string AtUri) => GetSetCount(STORE_LIKES, AtUri);

    public Task<long> GetReplyCount(string AtUri) => GetSetCount(STORE_REPLIES, AtUri);

    public Task<long> GetRepostCount(string AtUri) => GetSetCount(STORE_REPOSTS, AtUri);

    public Task<long> GetQuotePostCount(string AtUri) => GetSetCount(STORE_QUOTE_POSTS, AtUri);

    public async Task<long> GetTotalInteractionCount(string AtUri)
    {
        List<Task<long>> counters =
        [
            GetLikeCount(AtUri),
            GetReplyCount(AtUri),
            GetRepostCount(AtUri),
            GetQuotePostCount(AtUri),
        ];
        var counted = await Task.WhenAll(counters);
        return counted.Sum();
    }

    public Task AddLike(string AtUri, string FromDid) => AddToSet(STORE_LIKES, AtUri, FromDid);

    public Task AddReply(string AtUri, string ReplyAtUri) =>
        AddToSet(STORE_REPLIES, AtUri, ReplyAtUri);

    public Task AddRepost(string AtUri, string FromDid) => AddToSet(STORE_REPOSTS, AtUri, FromDid);

    public Task AddQuotePost(string AtUri, string PostAtUri) =>
        AddToSet(STORE_QUOTE_POSTS, AtUri, PostAtUri);

    public Task DeleteTopLevelPost(string AtUri)
    {
        return redis.WithTransaction(
            async (tr) =>
            {
                await tr.KeyDeleteAsync(RedisKey(STORE_LIKES, AtUri));
                await tr.KeyDeleteAsync(RedisKey(STORE_REPLIES, AtUri));
                await tr.KeyDeleteAsync(RedisKey(STORE_REPOSTS, AtUri));
                await tr.KeyDeleteAsync(RedisKey(STORE_QUOTE_POSTS, AtUri));
            }
        );
    }

    public Task RemoveLike(string AtUri, string FromDid) =>
        RemoveFromSet(STORE_LIKES, AtUri, FromDid);

    public Task RemoveReply(string AtUri, string ReplyAtUri) =>
        RemoveFromSet(STORE_REPLIES, AtUri, ReplyAtUri);

    public Task RemoveRepost(string AtUri, string FromDid) =>
        RemoveFromSet(STORE_REPOSTS, AtUri, FromDid);

    public Task RemoveQuotePost(string AtUri, string PostAtUri) =>
        RemoveFromSet(STORE_QUOTE_POSTS, AtUri, PostAtUri);

    private static string RedisKey(string store, string key)
    {
        return $"kbf:faststore:{{{key}}}:{store}";
    }

    private Task<long> GetSetCount(string store, string key)
    {
        return redis.SetLengthAsync(RedisKey(store, key));
    }

    private Task AddToSet(string store, string key, string value)
    {
        var rkey = RedisKey(store, key);
        return redis.WithTransaction(
            async (tr) =>
            {
                await tr.SetAddAsync(rkey, value);
                await tr.KeyExpireAsync(
                    rkey,
                    DateTime.UtcNow + TimeSpan.FromDays(3),
                    CommandFlags.FireAndForget
                );
            }
        );
    }

    private Task<bool> RemoveFromSet(string store, string key, string value)
    {
        return redis.SetRemoveAsync(RedisKey(store, key), value);
    }
}
