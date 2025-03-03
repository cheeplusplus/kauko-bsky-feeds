using EFCore.BulkExtensions;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Ingest.Workers;

public class JetstreamWorker(
    ILogger<JetstreamWorker> logger,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    FeedDbContext db,
    IngestMetrics metrics,
    FishyFlip.ATProtocol proto,
    IBskyCache bskyCache,
    IMemoryCache memCache,
    IJetstreamConsumer consumer
) : IHostedService
{
    private readonly BskyConfigBlock _bskyConfig = configuration
        .GetSection("BskyConfig")
        .Get<BskyConfigBlock>()!;
    private readonly IngestConfig _ingestConfig =
        configuration.GetSection("IngestConfig").Get<IngestConfig>() ?? new();
    private readonly CancellationTokenSource _readCancel = new();
    private DateTime _lastSave = DateTime.MinValue;
    private DateTime? _lastSaveMarker;
    private DateTime _lastCleanup = DateTime.MinValue;
    private int _saveFailureCount = 0;
    private readonly BulkInsertHolder _insertHolder = new(db, metrics);

    private int SaveMaxSec => _ingestConfig.SaveMaxSec;
    private int SaveMaxSize => _ingestConfig.SaveMaxSize;
    private List<string>? Collections =>
        _ingestConfig.SingleCollection != null ? [_ingestConfig.SingleCollection] : null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Begin to consume the reading channel
        _ = Task.Run(() => ReadChannel(_readCancel.Token), cancellationToken);

        // Start consuming
        await consumer.Start(
            getCursor: FetchLatestCursor,
            wantedCollections: Collections,
            cancellationToken: cancellationToken
        );

        // Start the timer for cleanup
        _lastCleanup = DateTime.Now + TimeSpan.FromSeconds(30); // Delay 30s
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await consumer.Stop();
        await _readCancel.CancelAsync(); // cancel after complete - may not be needed or should be later in the lifecycle
        await CommitDb(cancellationToken); // make sure we're up to date before leaving
    }

    private async Task<long?> FetchLatestCursor(CancellationToken cancellationToken = default)
    {
        long timeUs;
        if (_lastSaveMarker != null)
        {
            timeUs = _lastSaveMarker.Value.ToMicroseconds();
        }
        else
        {
            // Check all the tables belonging to real collections for the latest event we've seen
            // Filter so we can run different copies of ingest with different collections
            long? latestEvent = null;
            if (Collections == null || Collections.Contains(BskyConstants.COLLECTION_TYPE_POST))
            {
                var latest = await db
                    .Posts.OrderByDescending(o => o.EventTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latest != null && (!latestEvent.HasValue || latest.EventTimeUs > latestEvent))
                {
                    latestEvent = latest.EventTimeUs;
                }
            }
            if (Collections == null || Collections.Contains(BskyConstants.COLLECTION_TYPE_LIKE))
            {
                var latest = await db
                    .PostLikes.OrderByDescending(o => o.EventTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latest != null && (!latestEvent.HasValue || latest.EventTimeUs > latestEvent))
                {
                    latestEvent = latest.EventTimeUs;
                }
            }
            if (Collections == null || Collections.Contains(BskyConstants.COLLECTION_TYPE_REPOST))
            {
                var latest = await db
                    .PostReposts.OrderByDescending(o => o.EventTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latest != null && (!latestEvent.HasValue || latest.EventTimeUs > latestEvent))
                {
                    latestEvent = latest.EventTimeUs;
                }
            }

            if (!latestEvent.HasValue)
            {
                if (_ingestConfig?.ConsumeHistoricFeed ?? false)
                {
                    logger.LogInformation("No events found, starting from earliest available");
                    return 0;
                }
                else
                {
                    logger.LogInformation("No events found, starting from now");
                    return null;
                }
            }
            else
            {
                timeUs = latestEvent.Value;
            }
        }

        // Move back a little bit
        var offset = timeUs - ((long)TimeSpan.FromSeconds(1).TotalMicroseconds);

        logger.LogInformation(
            "Events found, resuming stream from {lastEventTime} ({offset})",
            timeUs,
            offset
        );

        return offset;
    }

    private async Task<List<string>?> FetchWantedDids(
        string collection,
        CancellationToken cancellationToken = default
    )
    {
        async Task EnsureLogin()
        {
            if (proto.IsAuthenticated)
            {
                return;
            }
            await proto.AuthenticateWithPasswordAsync(
                _bskyConfig.Auth.Username,
                _bskyConfig.Auth.Password,
                cancellationToken: cancellationToken
            );
        }

        return await memCache.GetOrCreateAsync(
            $"ingest_wanteddids_{collection}",
            async (_) =>
            {
                // Fetch wanted DIDs
                List<string> wantedDids = [];
                if (_ingestConfig.SingleCollection != null && _ingestConfig.Filter != null)
                {
                    if (
                        _ingestConfig.Filter.TryGetValue(
                            collection,
                            out IngestFilterConfig? targetFilter
                        )
                    )
                    {
                        if (targetFilter.ListUris != null)
                        {
                            foreach (var listUri in targetFilter.ListUris)
                            {
                                await EnsureLogin();

                                var listDids = await bskyCache.GetListMembers(
                                    proto,
                                    FishyFlip.Models.ATUri.Create(listUri),
                                    cancellationToken
                                );
                                wantedDids.AddRange(listDids.Select(s => s.ToString()));
                            }
                        }
                        if (targetFilter.Dids != null)
                        {
                            wantedDids.AddRange(targetFilter.Dids);
                        }
                    }
                }

                if (wantedDids.Count == 0)
                {
                    return null;
                }

                logger.LogInformation(
                    "Got list of wanted DIDs to filter ({wantedDidCount}) for collection {collection}",
                    wantedDids.Count,
                    collection
                );

                return wantedDids;
            },
            BskyCache.DEFAULT_OPTS
        );
    }

    private async void ReadChannel(CancellationToken cancellationToken)
    {
        try
        {
            // Continously consume from the channel until cancelled
            while (await consumer.ChannelReader.WaitToReadAsync(cancellationToken))
            {
                while (consumer.ChannelReader.TryRead(out var msg))
                {
                    try
                    {
                        await HandleMessage(msg, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // Continue and just drop the message, for now
                        logger.LogError(
                            ex,
                            "Encountered exception inside message writer on message {uri}",
                            msg.ToAtUri()
                        );
                    }

                    try
                    {
                        await TrySave(msg, cancellationToken);
                        _saveFailureCount = 0;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore
                    }
                    catch (OperationCanceledException)
                    {
                        // Break the loop
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Handle too many repeated failures
                        _saveFailureCount++;
                        if (_saveFailureCount > 3)
                        {
                            throw new Exception("Too many failures trying to save");
                        }

                        // Continue and try again next time
                        logger.LogError(
                            ex,
                            "Encountered exception saving after message {uri}",
                            msg.ToAtUri()
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read from channel");

            // This should be considered a fatal error at this point
            lifetime.StopApplication();
        }
    }

    private async Task TrySave(JetstreamMessage lastMessage, CancellationToken cancellationToken)
    {
        // Save every 10 seconds or every 10000 records
        if (
            DateTime.Now - TimeSpan.FromSeconds(SaveMaxSec) > _lastSave
            || _insertHolder.Size > SaveMaxSize
        )
        {
            logger.LogDebug("Committing to disk...");
            var saveCount = await CommitDb(cancellationToken);

            // Log on us catching up if we're behind
            if (_lastSaveMarker < (DateTime.UtcNow - TimeSpan.FromSeconds(60)))
            {
                logger.LogInformation(
                    "Catching up, {timespan} behind ({writes:N} writes/s)",
                    DateTime.UtcNow - lastMessage.MessageTime,
                    saveCount / (DateTime.Now - _lastSave).TotalSeconds
                );
            }

            _lastSave = DateTime.Now;
            _lastSaveMarker = lastMessage.MessageTime;
        }

        // Clean up every 10 minutes
        if (DateTime.Now - TimeSpan.FromMinutes(10) > _lastCleanup)
        {
            logger.LogInformation("Performing db cleanup");
            var threeDaysAgo = DateTime.UtcNow - TimeSpan.FromDays(3);

            using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var count1 = await db
                .Posts.Where(s => s.EventTime < threeDaysAgo)
                .ExecuteDeleteAsync(cancellationToken);
            var count2 = await db
                .PostLikes.Where(s => s.EventTime < threeDaysAgo)
                .ExecuteDeleteAsync(cancellationToken);
            var count3 = await db
                .PostQuotePosts.Where(s => s.EventTime < threeDaysAgo)
                .ExecuteDeleteAsync(cancellationToken);
            var count4 = await db
                .PostReplies.Where(s => s.EventTime < threeDaysAgo)
                .ExecuteDeleteAsync(cancellationToken);
            var count5 = await db
                .PostReposts.Where(s => s.EventTime < threeDaysAgo)
                .ExecuteDeleteAsync(cancellationToken);
            var count = count1 + count2 + count3 + count4 + count5;

            await transaction.CommitAsync(cancellationToken);

            _lastCleanup = DateTime.Now;
            logger.LogInformation("Cleanup complete, {count} records pruned", count);
        }
    }

    private async Task HandleMessage(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        metrics.IngestEvent(message.Commit?.Collection ?? "_unknown_", message.MessageTime);

        if (message.Commit?.Collection == BskyConstants.COLLECTION_TYPE_POST)
        {
            await HandleMessage_Post(message, cancellationToken);
        }
        else if (message.Commit?.Collection == BskyConstants.COLLECTION_TYPE_LIKE)
        {
            await HandleMessage_Like(message, cancellationToken);
        }
        else if (message.Commit?.Collection == BskyConstants.COLLECTION_TYPE_REPOST)
        {
            await HandleMessage_Repost(message, cancellationToken);
        }
    }

    private async Task HandleMessage_Post(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        if (message.Commit?.Operation == null)
        {
            return;
        }

        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            if (wantedDids != null && !wantedDids.Contains(message.Did))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch wanted DIDs list, continuing");
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            var post = message.ToDbPost();
            if (post != null)
            {
                var reply = message.AsDbPostReply();
                var quotePost = message.AsDbPostQuotePost();

                _insertHolder.Add(post, reply, quotePost);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            // Delete everything related to this post
            _insertHolder.DeletePost(message.Did, message.Commit.RecordKey);
        }
        else
        {
            // TODO: Support updates
            logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task HandleMessage_Like(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        if (message.Commit?.Record is not AppBskyFeedLike)
        {
            return;
        }

        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            var subjectDid = message.GetSubjectDid();
            if (wantedDids != null && subjectDid != null && !wantedDids.Contains(subjectDid))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch wanted DIDs list, continuing");
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            var like = message.AsDbPostLike();
            if (like != null)
            {
                _insertHolder.Add(like);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            _insertHolder.Delete(
                new PostRecordRef(message.Did, message.Commit.RecordKey),
                db =>
                    db.PostLikes.Where(w =>
                        w.LikeDid == message.Did && w.LikeRkey == message.Commit.RecordKey
                    )
            );
        }
        else
        {
            // TODO: Support updates
            logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task HandleMessage_Repost(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        if (message.Commit?.Record is not AppBskyFeedRepost commitRecord)
        {
            return;
        }

        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            var subjectDid = message.GetSubjectDid();
            if (wantedDids != null && subjectDid != null && !wantedDids.Contains(subjectDid))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch wanted DIDs list, continuing");
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            // await _fastStore.AddRepost(commitRecord.Subject.Uri, message.Did);
            var repost = message.AsDbPostRepost();
            if (repost != null)
            {
                _insertHolder.Add(repost);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            _insertHolder.Delete(
                new PostRecordRef(message.Did, message.Commit.RecordKey),
                db =>
                    db.PostReposts.Where(w =>
                        w.RepostDid == message.Did && w.RepostRkey == message.Commit.RecordKey
                    )
            );
        }
        else
        {
            // TODO: Support updates
            logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task<int> CommitDb(CancellationToken cancellationToken = default)
    {
        var (upserts, deletes) = await _insertHolder.Commit(cancellationToken);
        logger.LogDebug(
            "Committed {upserts} writes and {deletes} deletes to disk",
            upserts,
            deletes
        );
        return upserts + deletes;
    }
}

class BulkInsertHolder(FeedDbContext db, IngestMetrics metrics)
{
    private readonly Dictionary<PostRecordRef, Post> Posts = [];
    private readonly List<IQueryable<Post>> PostDeletes = [];
    private readonly Dictionary<PostRecordRef, PostLike> PostLikes = [];
    private readonly List<IQueryable<PostLike>> PostLikeDeletes = [];
    private readonly Dictionary<PostRecordRef, PostQuotePost> PostQuotePosts = [];
    private readonly List<IQueryable<PostQuotePost>> PostQuoteDeletes = [];
    private readonly Dictionary<PostRecordRef, PostReply> PostReplies = [];
    private readonly List<IQueryable<PostReply>> PostReplyDeletes = [];
    private readonly Dictionary<PostRecordRef, PostRepost> PostReposts = [];
    private readonly List<IQueryable<PostRepost>> PostRepostDeletes = [];

    public void Add(Post item, PostReply? reply, PostQuotePost? quotePost)
    {
        Posts.TryAdd(item.Ref, item);
        if (reply != null)
        {
            PostReplies.TryAdd(reply.Ref, reply);
        }
        if (quotePost != null)
        {
            PostQuotePosts.TryAdd(quotePost.Ref, quotePost);
        }
    }

    public void DeletePost(string did, string rkey)
    {
        var key = new PostRecordRef(did, rkey);

        PostDeletes.Add(db.Posts.Where(w => w.Did == did && w.Rkey == rkey));
        Posts.Remove(key);
        PostLikeDeletes.Add(db.PostLikes.Where(w => w.ParentDid == did && w.ParentRkey == rkey));
        PostLikes.Remove(key);
        PostQuoteDeletes.Add(
            db.PostQuotePosts.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        PostQuotePosts.Remove(key);
        PostReplyDeletes.Add(db.PostReplies.Where(w => w.ParentDid == did && w.ParentRkey == rkey));
        PostReplies.Remove(key);
        PostRepostDeletes.Add(
            db.PostReposts.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        PostReposts.Remove(key);
    }

    public void Add(PostLike item)
    {
        PostLikes.TryAdd(item.Ref, item);
    }

    public void Delete(PostRecordRef key, Func<FeedDbContext, IQueryable<PostLike>> deleteExpr)
    {
        PostLikeDeletes.Add(deleteExpr(db));
        PostLikes.Remove(key);
    }

    public void Add(PostRepost item)
    {
        PostReposts.TryAdd(item.Ref, item);
    }

    public void Delete(PostRecordRef key, Func<FeedDbContext, IQueryable<PostRepost>> deleteExpr)
    {
        PostRepostDeletes.Add(deleteExpr(db));
        PostReposts.Remove(key);
    }

    public int Size =>
        Posts.Count
        + PostDeletes.Count
        + PostLikes.Count
        + PostLikeDeletes.Count
        + PostQuotePosts.Count
        + PostQuoteDeletes.Count
        + PostReplies.Count
        + PostReplyDeletes.Count
        + PostReposts.Count
        + PostRepostDeletes.Count;

    public async Task<(int, int)> Commit(CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.BulkInsertOrUpdateAsync(Posts.Values, cancellationToken: cancellationToken);
        await db.BulkInsertOrUpdateAsync(PostLikes.Values, cancellationToken: cancellationToken);
        await db.BulkInsertOrUpdateAsync(
            PostQuotePosts.Values,
            cancellationToken: cancellationToken
        );
        await db.BulkInsertOrUpdateAsync(PostReplies.Values, cancellationToken: cancellationToken);
        await db.BulkInsertOrUpdateAsync(PostReposts.Values, cancellationToken: cancellationToken);

        int deletes = 0;
        foreach (var req in PostDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in PostLikeDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in PostQuoteDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in PostReplyDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var req in PostRepostDeletes)
        {
            deletes += await req.ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var upserts =
            Posts.Count
            + PostLikes.Count
            + PostQuotePosts.Count
            + PostReplies.Count
            + PostReposts.Count;

        metrics.TrackSave(
            DateTime.Now - startTime,
            Posts.Count,
            PostLikes.Count,
            PostQuotePosts.Count,
            PostReplies.Count,
            PostReposts.Count
        );

        // Clear after committing the transaction successfully
        Posts.Clear();
        PostDeletes.Clear();
        PostLikes.Clear();
        PostLikeDeletes.Clear();
        PostQuotePosts.Clear();
        PostQuoteDeletes.Clear();
        PostReplies.Clear();
        PostReplyDeletes.Clear();
        PostReposts.Clear();
        PostRepostDeletes.Clear();

        db.ChangeTracker.Clear();

        return (upserts, deletes);
    }
}
