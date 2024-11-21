using System.Threading.Channels;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Redis.FastStore;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Ingest.Workers;

public class JetstreamWorker : IHostedService
{
    private readonly ILogger<JetstreamWorker> _logger;
    private readonly FeedDbContext _db;
    private readonly IFastStore _fastStore;
    private readonly IJetstreamConsumer _consumer;
    private readonly bool _consumeFromHistoricFeed = false;
    private readonly Channel<JetstreamMessage> _channel =
        Channel.CreateUnbounded<JetstreamMessage>();
    private readonly CancellationTokenSource _channelCancel = new();
    private Post? _cursorEvent;
    private DateTime _lastSave = DateTime.MinValue;
    private DateTime? _lastSaveMarker;
    private DateTime _lastCleanup = DateTime.MinValue;

    public JetstreamWorker(
        ILogger<JetstreamWorker> logger,
        IConfiguration configuration,
        FeedDbContext db,
        IFastStore fastStore,
        IJetstreamConsumer consumer
    )
    {
        _logger = logger;
        _consumeFromHistoricFeed = configuration.GetValue<bool>("IngestConfig:ConsumeHistoricFeed");
        _db = db;
        _fastStore = fastStore;
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
            if (_consumeFromHistoricFeed)
            {
                _logger.LogInformation("No events found, starting from earliest available");
                return 0;
            }
            else
            {
                _logger.LogInformation("No events found, starting from now");
                return null;
            }
        }

        var offset = _cursorEvent.EventTimeUs - ((long)TimeSpan.FromSeconds(1).TotalMicroseconds);
        _logger.LogInformation(
            "Events found, resuming stream from {lastEventTime} ({offset})",
            _cursorEvent.EventTimeUs,
            offset
        );
        return offset;
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
        else if (message.Commit?.Collection == BskyConstants.COLLECTION_TYPE_LIKE)
        {
            await HandleMessage_Like(message, cancellationToken);
        }
        else if (message.Commit?.Collection == BskyConstants.COLLECTION_TYPE_REPOST)
        {
            await HandleMessage_Repost(message, cancellationToken);
        }

        // Save every 10 seconds
        if (DateTime.Now - TimeSpan.FromSeconds(10) > _lastSave)
        {
            var saveCount = await CommitDb(cancellationToken);

            // Log on us catching up if we're behind
            if (_lastSaveMarker < (DateTime.UtcNow - TimeSpan.FromSeconds(60)))
            {
                _logger.LogInformation(
                    "Catching up, {timespan} behind ({writes:N} writes/s, {posts:N} posts/s)",
                    DateTime.UtcNow - message.MessageTime,
                    saveCount / (DateTime.Now - _lastSave).TotalSeconds,
                    saveCount
                        / (
                            message.MessageTime - (_lastSaveMarker ?? DateTime.MinValue)
                        ).TotalSeconds
                );
            }

            _lastSave = DateTime.Now;
            _lastSaveMarker = message.MessageTime;
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

    private async Task HandleMessage_Post(
        JetstreamMessage message,
        CancellationToken cancellationToken
    )
    {
        if (message.Commit?.Operation == null)
        {
            return;
        }
        var messageAtUri = message.ToAtUri();

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            var post = message.ToDbPost();
            if (post != null)
            {
                try
                {
                    _db.Posts.Add(post);
                }
                catch (InvalidOperationException)
                {
                    // Just ignore dupes, not sure why they happen but they do
                }

                if (post.ReplyParentUri != null)
                {
                    await _fastStore.AddReply(post.ReplyParentUri, messageAtUri);
                }
                if (post.Embeds?.RecordUri != null)
                {
                    await _fastStore.AddQuotePost(post.Embeds.RecordUri, messageAtUri);
                }
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            var deletedPost = await _db.Posts.SingleOrDefaultAsync(
                w => w.Did == message.Did && w.Rkey == message.Commit.RecordKey,
                cancellationToken
            );
            if (deletedPost != null)
            {
                // Clean up
                if (deletedPost.ReplyParentUri != null)
                {
                    await _fastStore.RemoveReply(deletedPost.ReplyParentUri, messageAtUri);
                }
                if (deletedPost.Embeds?.RecordUri != null)
                {
                    await _fastStore.RemoveQuotePost(deletedPost.Embeds.RecordUri, messageAtUri);
                }
                await _fastStore.DeleteTopLevelPost(messageAtUri);

                _db.Posts.Remove(deletedPost);
            }

            /*await _db
                .Posts.Where(w => w.Did == message.Did && w.Rkey == message.Commit.RecordKey)
                .ExecuteDeleteAsync(cancellationToken);*/
        }
        else
        {
            // TODO: Support updates
            _logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task HandleMessage_Like(
        JetstreamMessage message,
        CancellationToken cancellationToken
    )
    {
        if (
            message.Commit?.Operation == null
            || message.Commit.Record is not AppBskyFeedLike commitRecord
        )
        {
            return;
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            await _fastStore.AddLike(commitRecord.Subject.Uri, message.Did);
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            await _fastStore.RemoveLike(commitRecord.Subject.Uri, message.Did);
        }
        else
        {
            // TODO: Support updates
            _logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task HandleMessage_Repost(
        JetstreamMessage message,
        CancellationToken cancellationToken
    )
    {
        if (
            message.Commit?.Operation == null
            || message.Commit.Record is not AppBskyFeedRepost commitRecord
        )
        {
            return;
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            await _fastStore.AddRepost(commitRecord.Subject.Uri, message.Did);
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            await _fastStore.AddRepost(commitRecord.Subject.Uri, message.Did);
        }
        else
        {
            // TODO: Support updates
            _logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task<int> CommitDb(CancellationToken cancellationToken = default)
    {
        var count = await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        _logger.LogDebug("Committed {count} changes to disk", count);
        return count;
    }
}
