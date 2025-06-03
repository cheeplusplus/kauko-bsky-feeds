using FishyFlip;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Ingest.Workers;

public partial class JetstreamWorker(
    ILogger<JetstreamWorker> logger,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    FeedDbContext db,
    IngestMetrics metrics,
    ATProtocol proto,
    IBskyCache bskyCache,
    HybridCache memCache,
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