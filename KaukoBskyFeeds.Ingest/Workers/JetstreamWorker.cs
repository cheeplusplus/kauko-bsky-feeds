using System.Collections.Concurrent;
using EFCore.BulkExtensions;
using FishyFlip;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using KaukoBskyFeeds.Shared.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace KaukoBskyFeeds.Ingest.Workers;

public class JetstreamWorker(
    ILogger<JetstreamWorker> logger,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    FeedDbContext db,
    IngestMetrics metrics,
    ATProtocol proto,
    IBskyCache bskyCache,
    HybridCache memCache,
    IFastCounter fastCounter,
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
    private int _saveFailureCount;
    private readonly BulkInsertHolder _insertHolder = new(
        db,
        metrics,
        fastCounter,
        configuration.GetSection("IngestConfig").Get<IngestConfig>() ?? new()
    );

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
            if (Collections == null || Collections.Contains(BskyConstants.CollectionTypePost))
            {
                var latest = await db
                    .Posts.OrderByDescending(o => o.EventTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latest != null && (!latestEvent.HasValue || latest.EventTimeUs > latestEvent))
                {
                    latestEvent = latest.EventTimeUs;
                }
            }
            if (Collections == null || Collections.Contains(BskyConstants.CollectionTypeLike))
            {
                var latest = await db
                    .PostLikes.OrderByDescending(o => o.EventTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latest != null && (!latestEvent.HasValue || latest.EventTimeUs > latestEvent))
                {
                    latestEvent = latest.EventTimeUs;
                }
            }
            if (Collections == null || Collections.Contains(BskyConstants.CollectionTypeRepost))
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
                if (_ingestConfig.ConsumeHistoricFeed)
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

            (
                await proto.AuthenticateWithPasswordResultAsync(
                    _bskyConfig.Auth.Username,
                    _bskyConfig.Auth.Password,
                    cancellationToken: cancellationToken
                )
            ).HandleResult();
        }

        return await memCache.GetOrCreateAsync(
            $"ingest_wanteddids_{collection}",
            async (_) =>
            {
                // Fetch wanted DIDs
                List<string> wantedDids = [];
                if (_ingestConfig.SingleCollection != null && _ingestConfig.Filter != null)
                {
                    if (_ingestConfig.Filter.TryGetValue(collection, out var targetFilter))
                    {
                        if (targetFilter.UserFollowsAndLists != null)
                        {
                            await EnsureLogin();

                            foreach (var userDidStr in targetFilter.UserFollowsAndLists)
                            {
                                var userDid = FishyFlip.Models.ATDid.Create(userDidStr);
                                if (userDid == null)
                                {
                                    continue;
                                }

                                // Get everyone this user is following
                                var userFollowingDids = await bskyCache.GetFollowing(
                                    proto,
                                    userDid,
                                    cancellationToken
                                );
                                wantedDids.AddRange(userFollowingDids.Select(s => s.ToString()));

                                // Get all of this user's lists
                                var userLists = await bskyCache.GetLists(
                                    proto,
                                    userDid,
                                    cancellationToken
                                );
                                foreach (var listDid in userLists)
                                {
                                    // Add list members
                                    var userListDids = await bskyCache.GetListMembers(
                                        proto,
                                        listDid,
                                        cancellationToken
                                    );
                                    wantedDids.AddRange(userListDids.Select(s => s.ToString()));
                                }
                            }
                        }
                        if (targetFilter.ListUris != null)
                        {
                            await EnsureLogin();

                            foreach (var listUri in targetFilter.ListUris)
                            {
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
            BskyCache.DefaultOpts,
            tags: ["ingest", "ingest/wantedDids"],
            cancellationToken: cancellationToken
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
        if (!_ingestConfig.DisableCleanup && DateTime.Now - TimeSpan.FromMinutes(10) > _lastCleanup)
        {
            var threeDaysAgo = DateTime.UtcNow - TimeSpan.FromDays(_ingestConfig.FeedHistoryDays);
            logger.LogInformation(
                "Performing db cleanup of anything older than {days} days",
                _ingestConfig.FeedHistoryDays
            );

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

        if (message.Commit?.Collection == BskyConstants.CollectionTypePost)
        {
            await HandleMessage_Post(message, cancellationToken);
        }
        else if (message.Commit?.Collection == BskyConstants.CollectionTypeLike)
        {
            await HandleMessage_Like(message, cancellationToken);
        }
        else if (message.Commit?.Collection == BskyConstants.CollectionTypeRepost)
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

        var shouldSaveToDb = true;
        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            var subjectDid = message.GetSubjectDid();
            if (
                wantedDids != null
                && !(
                    wantedDids.Contains(message.Did)
                    || (subjectDid != null && wantedDids.Contains(subjectDid))
                )
            )
            {
                shouldSaveToDb = false;
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
                _insertHolder.Add(like, shouldSaveToDb);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            _insertHolder.Delete(
                message.GetRef(),
                message.GetSubjectRef(),
                d =>
                    d.PostLikes.Where(w =>
                        w.LikeDid == message.Did && w.LikeRkey == message.Commit.RecordKey
                    ),
                shouldSaveToDb
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
        if (message.Commit?.Record is not AppBskyFeedRepost)
        {
            return;
        }

        var shouldSaveToDb = true;
        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            var subjectDid = message.GetSubjectDid();
            if (
                wantedDids != null
                && !(
                    wantedDids.Contains(message.Did)
                    || (subjectDid != null && wantedDids.Contains(subjectDid))
                )
            )
            {
                shouldSaveToDb=false;
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
                _insertHolder.Add(repost, shouldSaveToDb);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            _insertHolder.Delete(
                message.GetRef(),
                message.GetSubjectRef(),
                d =>
                    d.PostReposts.Where(w =>
                        w.RepostDid == message.Did && w.RepostRkey == message.Commit.RecordKey
                    ),
                shouldSaveToDb
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

class BulkInsertHolder(
    FeedDbContext db,
    IngestMetrics metrics,
    IFastCounter fastCounter,
    IngestConfig ingestConfig
)
{
    // Posts
    private readonly Dictionary<PostRecordRef, Post> _posts = [];
    private readonly List<IQueryable<Post>> _postDeletes = [];
    
    // Likes
    private readonly Dictionary<PostRecordRef, PostLike> _postLikes = [];
    private readonly ConcurrentDictionary<PostRecordRef, int> _postLikeCounter = [];
    private readonly List<IQueryable<PostLike>> _postLikeDeletes = [];
    private readonly List<PostRecordRef> _postLikeDeleteKeys = [];
    
    // Quote posts
    private readonly Dictionary<PostRecordRef, PostQuotePost> _postQuotePosts = [];
    private readonly List<IQueryable<PostQuotePost>> _postQuoteDeletes = [];
    private readonly ConcurrentDictionary<PostRecordRef, int> _postQuoteCounter = [];
    
    // Replies
    private readonly Dictionary<PostRecordRef, PostReply> _postReplies = [];
    private readonly List<IQueryable<PostReply>> _postReplyDeletes = [];
    private readonly ConcurrentDictionary<PostRecordRef, int> _postReplyCounter = [];
    
    // Reposts
    private readonly Dictionary<PostRecordRef, PostRepost> _postReposts = [];
    private readonly List<IQueryable<PostRepost>> _postRepostDeletes = [];
    private readonly ConcurrentDictionary<PostRecordRef, int> _postRepostCounter = [];
    private readonly List<PostRecordRef> _postRepostDeleteKeys = [];

    public void Add(Post item, PostReply? reply, PostQuotePost? quotePost)
    {
        _posts.TryAdd(item.Ref, item);
        if (reply != null)
        {
            _postReplies.TryAdd(reply.Ref, reply);
            _postReplyCounter.AddOrUpdate(item.Ref, 1, (p, i) => i + 1);
        }

        if (quotePost != null)
        {
            _postQuotePosts.TryAdd(quotePost.Ref, quotePost);
            _postQuoteCounter.AddOrUpdate(item.Ref, 1, (p, i) => i + 1);
        }
    }

    public void DeletePost(string did, string rkey)
    {
        var key = new PostRecordRef(did, rkey);

        _postDeletes.Add(db.Posts.Where(w => w.Did == did && w.Rkey == rkey));
        _posts.Remove(key);
        
        _postLikeDeletes.Add(db.PostLikes.Where(w => w.ParentDid == did && w.ParentRkey == rkey));
        _postLikes.Remove(key);
        _postLikeCounter.Remove(key, out _);
        _postLikeDeleteKeys.Add(key);
        
        _postQuoteDeletes.Add(
            db.PostQuotePosts.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        _postQuotePosts.Remove(key);
        _postQuoteCounter.Remove(key, out _);

        _postReplyDeletes.Add(
            db.PostReplies.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        _postReplies.Remove(key);
        _postReplyCounter.Remove(key, out _);
        
        _postRepostDeletes.Add(
            db.PostReposts.Where(w => w.ParentDid == did && w.ParentRkey == rkey)
        );
        _postReposts.Remove(key);
        _postRepostCounter.Remove(key, out _);
    }

    public void Add(PostLike item, bool saveToDb)
    {
        if (saveToDb)
        {
            _postLikes.TryAdd(item.Ref, item);
        }
        _postLikeCounter.AddOrUpdate(item.ParentRef, 1, (p, i) => i + 1);
    }

    public void Delete(PostRecordRef? key, PostRecordRef? parentKey, Func<FeedDbContext, IQueryable<PostLike>> deleteExpr, bool saveToDb)
    {
        if (saveToDb)
        {
            _postLikeDeletes.Add(deleteExpr(db));
            if (key != null)
            {
                _postLikes.Remove(key);
            }
        }

        if (parentKey != null)
        {
            _postLikeCounter.AddOrUpdate(parentKey, -1, (p, i) => i - 1);
        }
    }

    public void Add(PostRepost item, bool saveToDb)
    {
        if (saveToDb)
        {
            _postReposts.TryAdd(item.Ref, item);
        }
    }

    public void Delete(PostRecordRef? key, PostRecordRef? parentKey, Func<FeedDbContext, IQueryable<PostRepost>> deleteExpr, bool saveToDb)
    {
        if (saveToDb)
        {
            _postRepostDeletes.Add(deleteExpr(db));
            if (key != null)
            {
                _postReposts.Remove(key);
            }
        }

        if (parentKey != null)
        {
            _postRepostCounter.AddOrUpdate(parentKey, -1, (p, i) => i - 1);
        }
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

        var redisChunks = new Dictionary<string, (ConcurrentDictionary<PostRecordRef, int>, List<PostRecordRef>)>()
        {
            {FastCounterKeys.Like, (_postLikeCounter, _postLikeDeleteKeys)},
            {FastCounterKeys.Quote, (_postQuoteCounter, [])},
            {FastCounterKeys.Reply, (_postReplyCounter, [])},
            {FastCounterKeys.Repost, (_postRepostCounter, _postRepostDeleteKeys)},
        };
        
        foreach (var chunk in redisChunks)
        {
            var (chunkCounters, chunkDeletes) = chunk.Value;
            foreach (var item in chunkCounters)
            {
                await fastCounter.Increment(
                    FastCounterKeys.Post(item.Key.Did, item.Key.Rkey),
                    chunk.Key,
                    item.Value,
                    TimeSpan.FromDays(ingestConfig.FeedHistoryDays),
                    ExpireWhen.HasNoExpiry // the key should never persist more than FeedHistoryDays days (better would be abs date via post creationDate but oh well) 
                );
            }

            foreach (var key in chunkDeletes)
            {
                await fastCounter.Delete(FastCounterKeys.Post(key.Did, key.Rkey));
            }
        }

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
        _postLikeCounter.Clear();
        _postLikeDeletes.Clear();
        _postLikeDeleteKeys.Clear();
        
        _postQuotePosts.Clear();
        _postQuoteDeletes.Clear();
        _postQuoteCounter.Clear();
        
        _postReplies.Clear();
        _postReplyDeletes.Clear();
        _postReplyCounter.Clear();
        
        _postReposts.Clear();
        _postRepostDeletes.Clear();
        _postRepostCounter.Clear();
        _postRepostDeleteKeys.Clear();

        db.ChangeTracker.Clear();

        return (upserts, deletes);
    }
}
