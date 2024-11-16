using System.Threading.Channels;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Ingest.Workers;

public class JetstreamWorker : IHostedService
{
    private readonly ILogger<JetstreamWorker> _logger;
    private readonly FeedDbContext _db;
    private readonly IJetstreamConsumer _consumer;
    private readonly Channel<JetstreamMessage> _channel =
        Channel.CreateUnbounded<JetstreamMessage>();
    private readonly CancellationTokenSource _channelCancel = new();
    private Post? _cursorEvent;
    private DateTime _lastSave = DateTime.MinValue;
    private DateTime _lastCleanup = DateTime.MinValue;

    public JetstreamWorker(
        ILogger<JetstreamWorker> logger,
        FeedDbContext db,
        IJetstreamConsumer consumer
    )
    {
        _logger = logger;
        _db = db;
        _consumer = consumer;
        _consumer.Message += (_, msg) => _channel.Writer.TryWrite(msg);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Begin to consume the reading channel
        _ = Task.Run(() => ReadChannel(_channelCancel.Token), cancellationToken);

        // Start consuming
        await _consumer.Start(FetchLatestCursor, cancellationToken: cancellationToken);

        // Start the timer for cleanup
        _lastCleanup = DateTime.Now + TimeSpan.FromSeconds(30); // Delay 30s
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _consumer.Stop();
        _channel.Writer.TryComplete();
        await _channelCancel.CancelAsync(); // cancel after complete - may not be needed or should be later in the lifecycle
        await CommitDb(cancellationToken); // make sure we're up to date before leaving
    }

    private long? FetchLatestCursor()
    {
        _cursorEvent = _db.Posts.OrderByDescending(o => o.EventTime).FirstOrDefault();
        if (_cursorEvent == null)
        {
            _logger.LogInformation("No events found, starting new stream");
            return null;
        }

        _logger.LogInformation(
            "Events found, resuming stream from {lastEventTime}",
            _cursorEvent.EventTimeUs
        );
        return _cursorEvent.EventTimeUs;
    }

    private async void ReadChannel(CancellationToken cancellationToken)
    {
        // Continously consume from the channel until cancelled
        await foreach (var msg in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await HandleMessage(msg, cancellationToken);
            }
            catch (Exception ex)
            {
                // Continue and just drop the message, for now
                _logger.LogError(
                    ex,
                    "Encountered exception inside message writer on message {uri}",
                    msg.ToAtUri()
                );
            }
        }
    }

    private async Task HandleMessage(JetstreamMessage message, CancellationToken cancellationToken)
    {
        if (message.Commit?.Collection == BskyConstants.COLLECTION_TYPE_POST)
        {
            await HandleMessage_Post(message, cancellationToken);
        }
    }

    private async Task HandleMessage_Post(
        JetstreamMessage message,
        CancellationToken cancellationToken
    )
    {
        if (message.Commit?.Operation == null)
        {
            return;
        }

        if (
            message.Commit.Operation == JetstreamOperation.Create
            || message.Commit.Operation == JetstreamOperation.Update
        )
        {
            var post = message.ToDbPost();
            if (post != null)
            {
                // Detect duplicates using the cursor event
                // Ideally we'd upsert or something more useful/correct
                if (post.ToUri() != _cursorEvent?.ToUri())
                {
                    _db.Add(post);
                }
                else
                {
                    _logger.LogInformation("Got duplicate record {rec}", post);
                }
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            // Directly execute the delete so we don't have to find it first
            await _db
                .Posts.Where(w => w.Did == message.Did && w.Rkey == message.Commit.RecordKey)
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Save every 10 seconds
        if (DateTime.Now - TimeSpan.FromSeconds(10) > _lastSave)
        {
            await CommitDb(cancellationToken);
            _lastSave = DateTime.Now;

            if (message.MessageTime < (DateTime.UtcNow - TimeSpan.FromMinutes(30)))
            {
                _logger.LogInformation("Caught up to {date}", message.MessageTime);
            }
        }

        // Clean up every 10 minutes
        if (DateTime.Now - TimeSpan.FromMinutes(10) > _lastCleanup)
        {
            _logger.LogInformation("Performing db cleanup");
            var threeDaysAgo = DateTime.UtcNow - TimeSpan.FromDays(3);
            var count = await _db
                .Posts.Where(s => s.EventTime < threeDaysAgo)
                .ExecuteDeleteAsync(cancellationToken);
            _lastCleanup = DateTime.Now;
            _logger.LogInformation("Cleanup complete, {count} records pruned", count);
        }
    }

    private async Task CommitDb(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _db.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Committed {count} changes to disk", count);
        }
        catch (DbUpdateException due)
        {
            // This is a sucky way to do this, but we essentially don't care about changes so we'll just drop them
            // We should and should support upserts but all the upsert libraries break with JSON columns right now
            if (due.InnerException is SqliteException ie)
            {
                if (ie.SqliteExtendedErrorCode == 1555)
                {
                    foreach (var fe in due.Entries.Where(w => w.State == EntityState.Added))
                    {
                        _logger.LogWarning("Got duplicate entry, detatching");
                        fe.State = EntityState.Detached; // detatch it
                    }
                    await _db.SaveChangesAsync(cancellationToken);
                    return;
                }
            }
            throw;
        }
    }
}
