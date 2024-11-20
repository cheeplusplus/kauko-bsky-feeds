using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class LofiWorker(
    ILogger<LofiWorker> logger,
    IConfiguration config,
    ATProtocol proto,
    BskyCache cache,
    IJetstreamConsumer jsc
) : IHostedService
{
    private Session? _session;
    private List<string>? _following;
    private readonly BskyConfigAuth bskyAuthConfig =
        config.GetSection("BskyConfig:Auth").Get<BskyConfigAuth>()
        ?? throw new Exception("Could not read config");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in");
        }

        _following = (await cache.GetFollowing(proto, _session.Did, cancellationToken))
            .Select(s => s.ToString())
            .ToList();
        jsc.Message += OnMessage;

        Console.WriteLine("Jamming on the feed...");

        await jsc.Start(
            getCursor: () =>
                new DateTimeOffset(
                    DateTime.UtcNow - TimeSpan.FromMinutes(10)
                ).ToUnixTimeMilliseconds() * 1000,
            wantedCollections: [BskyConstants.COLLECTION_TYPE_POST],
            cancellationToken: cancellationToken
        );
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await jsc.Stop();
    }

    private void OnMessage(object? sender, JetstreamMessage e)
    {
        if (
            e.Commit?.Collection != BskyConstants.COLLECTION_TYPE_POST
            || e.Commit?.Operation != JetstreamOperation.Create
        )
        {
            return;
        }
        if (
            (e.Did != _session?.Did.ToString())
            && (_following == null || !_following.Contains(e.Did))
        )
        {
            return;
        }
        if (e.Commit?.Record is AppBskyFeedPost post)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandlePost(e.Did, e.Commit.RecordKey, post);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Got error handling post");
                }
            });
        }
    }

    private async Task HandlePost(string did, string rkey, AppBskyFeedPost post)
    {
        var profile = await cache.GetProfile(proto, ATDid.Create(did)!);
        var displayName = profile?.DisplayName ?? "?";
        var handle = profile?.Handle ?? did;

        var now = post.CreatedAt.ToLocalTime().ToShortTimeString();

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.Write($"[{now}] ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{displayName} ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"(@{handle}) ");

        Console.ForegroundColor = ConsoleColor.Gray;
        if (post.Reply != null)
        {
            Console.Write($"replied to ");

            var replyParent = new ATUri(post.Reply.Parent.Uri);
            var replyProfile = await cache.GetProfile(proto, replyParent.Did!);
            var replyHandle = replyProfile?.Handle ?? replyParent.Did?.ToString() ?? "?";

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"@{replyHandle}");
        }
        else
        {
            Console.Write("posted");
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(": ");

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write(TerminalURL("(web)", $"https://bsky.app/profile/{did}/post/{rkey}"));
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(post.Text))
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("    (no text)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(post.Text);
        }

        if (post.Embed != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write("  Embeds: ");
            Console.WriteLine(
                JsonSerializer.Serialize(
                    post.Embed,
                    new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition =
                            JsonIgnoreCondition.WhenWritingDefault
                            | JsonIgnoreCondition.WhenWritingNull,
                    }
                )
            );
        }
    }

    private async Task EnsureLogin(CancellationToken cancellationToken = default)
    {
        if (_session == null || !proto.IsAuthenticated)
        {
            _session =
                proto.Session
                ?? await proto.AuthenticateWithPasswordAsync(
                    bskyAuthConfig.Username,
                    bskyAuthConfig.Password,
                    cancellationToken: cancellationToken
                )
                ?? throw new Exception("Failed to login");
        }
    }

    private static string TerminalURL(string caption, string url) =>
        $"\u001B]8;;{url}\a{caption}\u001B]8;;\a";
}
