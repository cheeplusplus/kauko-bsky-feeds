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

        jsc.Message += OnMessage;

        long? backfillCursor =
            _lofiConfig.BackfillMinutes.HasValue && _lofiConfig.BackfillMinutes.Value > 0
                ? new DateTimeOffset(
                    DateTime.UtcNow - TimeSpan.FromMinutes(_lofiConfig.BackfillMinutes.Value)
                ).ToUnixTimeMilliseconds() * 1000
                : null;

        await jsc.Start(
            getCursor: () => _lastCursor ?? backfillCursor,
            cancellationToken: cancellationToken
        );

        Console.WriteLine("Jamming on the feed...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        await jsc.Stop();
    }

    private void OnMessage(object? sender, JetstreamMessage e)
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
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandlePost(e, post);
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException)
                    {
                        // Do nothing
                    }
                    else
                    {
                        logger.LogError(ex, "Got error handling post");
                    }
                }
            });
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
            .WhereNotNull();

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
                ?? await proto.AuthenticateWithPasswordAsync(
                    _bskyAuthConfig.Username,
                    _bskyAuthConfig.Password,
                    cancellationToken: cancellationToken
                )
                ?? throw new Exception("Failed to login");
        }
    }
}
