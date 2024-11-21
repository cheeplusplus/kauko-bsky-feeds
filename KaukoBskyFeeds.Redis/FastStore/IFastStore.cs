namespace KaukoBskyFeeds.Redis.FastStore;

public interface IFastStore
{
    Task<long> GetLikeCount(string AtUri);
    Task<long> GetReplyCount(string AtUri);
    Task<long> GetRepostCount(string AtUri);
    Task<long> GetQuotePostCount(string AtUri);
    Task<long> GetTotalInteractionCount(string AtUri);

    Task AddLike(string AtUri, string FromDid);
    Task AddReply(string AtUri, string ReplyAtUri);
    Task AddRepost(string AtUri, string FromDid);
    Task AddQuotePost(string AtUri, string PostAtUri);

    Task DeleteTopLevelPost(string AtUri);

    Task RemoveLike(string AtUri, string FromDid);
    Task RemoveReply(string AtUri, string ReplyAtUri);
    Task RemoveRepost(string AtUri, string FromDid);
    Task RemoveQuotePost(string AtUri, string PostAtUri);
}
