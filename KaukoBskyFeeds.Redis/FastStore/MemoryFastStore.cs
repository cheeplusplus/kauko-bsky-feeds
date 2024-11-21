namespace KaukoBskyFeeds.Redis.FastStore;

public class MemoryFastStore : IFastStore
{
    private readonly Dictionary<string, HashSet<string>> _likes = new();
    private readonly Dictionary<string, HashSet<string>> _replies = new();
    private readonly Dictionary<string, HashSet<string>> _reposts = new();
    private readonly Dictionary<string, HashSet<string>> _quotePosts = new();

    public Task<long> GetLikeCount(string AtUri)
    {
        return Task.FromResult(GetSubdictCount(_likes, AtUri));
    }

    public Task<long> GetReplyCount(string AtUri)
    {
        return Task.FromResult(GetSubdictCount(_replies, AtUri));
    }

    public Task<long> GetRepostCount(string AtUri)
    {
        return Task.FromResult(GetSubdictCount(_reposts, AtUri));
    }

    public Task<long> GetQuotePostCount(string AtUri)
    {
        return Task.FromResult(GetSubdictCount(_quotePosts, AtUri));
    }

    public async Task<long> GetTotalInteractionCount(string AtUri)
    {
        var lc = await GetLikeCount(AtUri);
        var rc = await GetReplyCount(AtUri);
        var rpc = await GetRepostCount(AtUri);
        var qpc = await GetQuotePostCount(AtUri);
        return lc + rc + rpc + qpc;
    }

    public Task AddLike(string AtUri, string FromDid)
    {
        AddSubdict(_likes, AtUri, FromDid);
        return Task.CompletedTask;
    }

    public Task AddReply(string AtUri, string ReplyAtUri)
    {
        AddSubdict(_replies, AtUri, ReplyAtUri);
        return Task.CompletedTask;
    }

    public Task AddRepost(string AtUri, string FromDid)
    {
        AddSubdict(_reposts, AtUri, FromDid);
        return Task.CompletedTask;
    }

    public Task AddQuotePost(string AtUri, string PostAtUri)
    {
        AddSubdict(_quotePosts, AtUri, PostAtUri);
        return Task.CompletedTask;
    }

    public Task DeleteTopLevelPost(string AtUri)
    {
        _likes.Remove(AtUri);
        _replies.Remove(AtUri);
        _reposts.Remove(AtUri);
        _quotePosts.Remove(AtUri);

        return Task.CompletedTask;
    }

    public Task RemoveLike(string AtUri, string FromDid)
    {
        RemoveSubdict(_likes, AtUri, FromDid);
        return Task.CompletedTask;
    }

    public Task RemoveReply(string AtUri, string ReplyAtUri)
    {
        RemoveSubdict(_replies, AtUri, ReplyAtUri);
        return Task.CompletedTask;
    }

    public Task RemoveRepost(string AtUri, string FromDid)
    {
        RemoveSubdict(_reposts, AtUri, FromDid);
        return Task.CompletedTask;
    }

    public Task RemoveQuotePost(string AtUri, string PostAtUri)
    {
        RemoveSubdict(_quotePosts, AtUri, PostAtUri);
        return Task.CompletedTask;
    }

    private static long GetSubdictCount(Dictionary<string, HashSet<string>> dict, string key)
    {
        if (dict.TryGetValue(key, out var likes))
        {
            return likes.Count;
        }
        return 0;
    }

    private static void AddSubdict(
        Dictionary<string, HashSet<string>> dict,
        string key,
        string value
    )
    {
        if (!dict.ContainsKey(key))
        {
            dict.Add(key, []);
        }
        dict[key].Add(value);
    }

    private static void RemoveSubdict(
        Dictionary<string, HashSet<string>> dict,
        string key,
        string value
    )
    {
        if (!dict.ContainsKey(key))
        {
            return;
        }
        dict[key].Remove(value);
    }
}
