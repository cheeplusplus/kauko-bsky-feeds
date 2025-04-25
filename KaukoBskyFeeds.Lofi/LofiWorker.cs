using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Lofi;

public class LofiWorker(
    ILogger<LofiWorker> logger,
    IConfiguration config,
    ATProtocol proto,
    BskyCache cache,
    IJetstreamConsumer jsc
) : IHostedService
{
    private readonly BskyConfigAuth _bskyAuthConfig =
        config.GetSection("BskyConfig:Auth").Get<BskyConfigAuth>()
        ?? throw new Exception("Could not read config");
    private readonly LofiConfig _lofiConfig =
        config.GetSection("LofiConfig").Get<LofiConfig>()
        ?? throw new Exception("Could not read config");
    private readonly CancellationTokenSource _cts = new();
    private readonly object _printLock = new();
    private Session? _session;
    private List<string>? _following;
    private long? _lastCursor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in");
        }

        if (_lofiConfig.CruiseOwnFeed)
        {
            _following = (await cache.GetFollowing(proto, _session.Did, cancellationToken))
                .Select(s => s.ToString())
                .ToList();
        }

        _ = Task.Run(() => ReadChannel(), cancellationToken);

        long? backfillCursor =
            _lofiConfig.BackfillMinutes.HasValue && _lofiConfig.BackfillMinutes.Value > 0
                ? (
                    DateTime.UtcNow - TimeSpan.FromMinutes(_lofiConfig.BackfillMinutes.Value)
                ).ToMicroseconds()
                : null;

        await jsc.Start(
            getCursor: (_) => Task.FromResult(_lastCursor ?? backfillCursor),
            wantedCollections: [BskyConstants.COLLECTION_TYPE_POST],
            cancellationToken: cancellationToken
        );

        logger.LogInformation("Jamming on the feed...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        await jsc.Stop();
    }

    private async void ReadChannel()
    {
        try
        {
            // Continously consume from the channel until cancelled
            while (await jsc.ChannelReader.WaitToReadAsync(_cts.Token))
            {
                while (jsc.ChannelReader.TryRead(out var msg))
                {
                    try
                    {
                        await HandleMessage(msg);
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
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read from channel");
        }
    }

    private async Task HandleMessage(JetstreamMessage e)
    {
        _lastCursor = e.TimeMicroseconds;

        if (e.Commit == null)
        {
            return;
        }
        if (
            e.Commit.Collection != BskyConstants.COLLECTION_TYPE_POST
            || e.Commit.Operation != JetstreamOperation.Create
        )
        {
            return;
        }
        if (
            _lofiConfig.CruiseOwnFeed
            && (e.Did != _session?.Did.ToString())
            && (_following == null || !_following.Contains(e.Did))
        )
        {
            return;
        }
        if (e.Commit.Record is AppBskyFeedPost post)
        {
            await HandlePost(e, post);
        }
    }

    private async Task HandlePost(JetstreamMessage msg, AppBskyFeedPost post)
    {
        if (post.Reply != null)
        {
            // Handle config checks
            if (!_lofiConfig.CruiseOwnFeed || _lofiConfig.HideReplies)
            {
                return;
            }
            if (_lofiConfig.HideRepliesToNotFollowing && _following != null)
            {
                var parentDid = ATUri.Create(post.Reply.Parent.Uri).Did?.ToString();
                var rootDid = ATUri.Create(post.Reply.Root.Uri).Did?.ToString();
                if (
                    (parentDid != null && !_following.Contains(parentDid))
                    || (rootDid != null && !_following.Contains(rootDid))
                )
                {
                    return;
                }
            }
        }

        var posts = new string?[] { msg.ToAtUri(), post.Reply?.Parent?.Uri, post.Reply?.Root?.Uri }
            .WhereNotNull()
            .Select(ATUri.Create)
            .WhereNotNull()
            .ToList();

        var hydratedReq = await proto.Feed.GetPostsAsync(posts, _cts.Token);
        var hydrated = hydratedReq.HandleResult();
        var msgPost = hydrated.Posts.SingleOrDefault(s => s.Uri.ToString() == msg.ToAtUri());
        if (msgPost == null)
        {
            // failed to hydrate
            return;
        }

        var replyParentPost = hydrated.Posts.SingleOrDefault(s =>
            s.Uri.ToString() == post.Reply?.Parent?.Uri
        );
        var replyRootPost = hydrated.Posts.SingleOrDefault(s =>
            s.Uri.ToString() == post.Reply?.Root?.Uri
        );

        var report = new LofiReport(msgPost, replyParentPost, replyRootPost);

        // Ensure we're only printing one post at a time
        lock (_printLock)
        {
            report.Print(_lofiConfig);
        }
    }

    private async Task EnsureLogin(CancellationToken cancellationToken = default)
    {
        if (_session == null || !proto.IsAuthenticated)
        {
            _session =
                proto.Session
                ?? (
                    await proto.AuthenticateWithPasswordResultAsync(
                        _bskyAuthConfig.Username,
                        _bskyAuthConfig.Password,
                        cancellationToken: cancellationToken
                    )
                ).HandleResult()
                ?? throw new Exception("Failed to login");
        }
    }
}
