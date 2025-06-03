using EFCore.BulkExtensions;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Ingest.Workers;

internal class BulkInsertHolder(FeedDbContext db, IngestMetrics metrics)
{
    private readonly Dictionary<PostRecordRef, Post> _posts = [];
    private readonly List<IQueryable<Post>> _postDeletes = [];
    private readonly Dictionary<PostRecordRef, PostLike> _postLikes = [];
    private readonly List<IQueryable<PostLike>> _postLikeDeletes = [];
    private readonly Dictionary<PostRecordRef, PostQuotePost> _postQuotePosts = [];
    private readonly List<IQueryable<PostQuotePost>> _postQuoteDeletes = [];
    private readonly Dictionary<PostRecordRef, PostReply> _postReplies = [];
    private readonly List<IQueryable<PostReply>> _postReplyDeletes = [];
    private readonly Dictionary<PostRecordRef, PostRepost> _postReposts = [];
    private readonly List<IQueryable<PostRepost>> _postRepostDeletes = [];

    public void Add(Post item, PostReply? reply, PostQuotePost? quotePost)
    {
        _posts.TryAdd(item.Ref, item);
        if (reply != null)
        {
            _postReplies.TryAdd(reply.Ref, reply);
        }

        if (quotePost != null)
        {
            _postQuotePosts.TryAdd(quotePost.Ref, quotePost);
        }
    }

    public void DeletePost(string did, string rkey)
    {
        var key = new PostRecordRef(did, rkey);

        _postDeletes.Add(db.Posts.Where(w => w.Did == did && w.Rkey == rkey));
        _posts.Remove(key);
        _postLikeDeletes.Add(db.PostLikes.Where(w => w.ParentDid == did && w.ParentRkey == rkey));
        _postLikes.Remove(key);
        _postQuoteDeletes.Add(
            db.PostQuotePosts.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        _postQuotePosts.Remove(key);
        _postReplyDeletes.Add(
            db.PostReplies.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        _postReplies.Remove(key);
        _postRepostDeletes.Add(
            db.PostReposts.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        _postReposts.Remove(key);
    }

    public void Add(PostLike item)
    {
        _postLikes.TryAdd(item.Ref, item);
    }

    public void Delete(PostRecordRef key, Func<FeedDbContext, IQueryable<PostLike>> deleteExpr)
    {
        _postLikeDeletes.Add(deleteExpr(db));
        _postLikes.Remove(key);
    }

    public void Add(PostRepost item)
    {
        _postReposts.TryAdd(item.Ref, item);
    }

    public void Delete(PostRecordRef key, Func<FeedDbContext, IQueryable<PostRepost>> deleteExpr)
    {
        _postRepostDeletes.Add(deleteExpr(db));
        _postReposts.Remove(key);
    }

    public int Size =>
        _posts.Count
        + _postDeletes.Count
        + _postLikes.Count
        + _postLikeDeletes.Count
        + _postQuotePosts.Count
        + _postQuoteDeletes.Count
        + _postReplies.Count
        + _postReplyDeletes.Count
        + _postReposts.Count
        + _postRepostDeletes.Count;

    public async Task<(int, int)> Commit(CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.BulkInsertOrUpdateAsync(_posts.Values, cancellationToken: cancellationToken);
        await db.BulkInsertOrUpdateAsync(_postLikes.Values, cancellationToken: cancellationToken);
        await db.BulkInsertOrUpdateAsync(
            _postQuotePosts.Values,
            cancellationToken: cancellationToken
        );
        await db.BulkInsertOrUpdateAsync(_postReplies.Values, cancellationToken: cancellationToken);
        await db.BulkInsertOrUpdateAsync(_postReposts.Values, cancellationToken: cancellationToken);

        int deletes = 0;
        foreach (var req in _postDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in _postLikeDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in _postQuoteDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in _postReplyDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in _postRepostDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var upserts =
            _posts.Count
            + _postLikes.Count
            + _postQuotePosts.Count
            + _postReplies.Count
            + _postReposts.Count;

        metrics.TrackSave(
            DateTime.Now - startTime,
            _posts.Count,
            _postLikes.Count,
            _postQuotePosts.Count,
            _postReplies.Count,
            _postReposts.Count
        );

        // Clear after committing the transaction successfully
        _posts.Clear();
        _postDeletes.Clear();
        _postLikes.Clear();
        _postLikeDeletes.Clear();
        _postQuotePosts.Clear();
        _postQuoteDeletes.Clear();
        _postReplies.Clear();
        _postReplyDeletes.Clear();
        _postReposts.Clear();
        _postRepostDeletes.Clear();

        db.ChangeTracker.Clear();

        return (upserts, deletes);
    }
}