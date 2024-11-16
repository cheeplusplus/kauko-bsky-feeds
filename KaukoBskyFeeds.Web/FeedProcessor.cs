using System.Globalization;
using System.Text.Json;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Feeds;
using KaukoBskyFeeds.Feeds.Config;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace KaukoBskyFeeds.Web;

public class FeedProcessor
{
    private readonly ILogger<FeedProcessor> _logger;
    private readonly IBskyCache _cache;
    private readonly BskyConfigBlock _config;
    private readonly ATProtocol _proto;
    private Session? _session;
    private readonly Dictionary<string, IFeed> _feeds = [];

    public FeedProcessor(
        ILoggerFactory loggerFactory,
        IConfiguration configuration,
        IBskyCache cache
    )
    {
        _logger = loggerFactory.CreateLogger<FeedProcessor>();
        _cache = cache;

        _config =
            configuration.GetSection("BskyConfig").Get<BskyConfigBlock>()
            ?? throw new Exception("Failed to load bsky config");

        var protoLogger = loggerFactory.CreateLogger("AtProto");
        _proto = new ATProtocolBuilder()
            .EnableAutoRenewSession(true)
            .WithLogger(protoLogger)
            .Build();

        // Build available feeds
        foreach (var feed in _config.FeedProcessors)
        {
            // TODO: Figure out a better way to get a concrete derived type out of the configuration
            var configSection =
                configuration.GetSection($"BskyConfig:FeedProcessors:{feed.Key}:Config")
                ?? throw new Exception("Failed to find feed configuration");

            IFeed inst = feed.Value.Type switch
            {
                "TimelineMinusList" => new TimelineMinusList(
                    loggerFactory.CreateLogger<TimelineMinusList>(),
                    _proto,
                    configSection.Get<TimelineMinusListFeedConfig>()
                        ?? throw new Exception("Failed to parse feed configuration"),
                    _cache
                ),
                _ => throw new Exception("Unknown feed type"),
            };
            _feeds.Add(
                $"{_config.Identity.PublishedAtUri}/{Constants.FeedType.Generator}/{feed.Key}",
                inst
            );
        }
    }

    public async Task<
        Results<NotFound, UnauthorizedHttpResult, Ok<CustomSkeletonFeed>>
    > GetFeedSkeleton(
        [FromHeader] string? authorization,
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_feeds.TryGetValue(feed, out IFeed? feedInstance))
        {
            _logger.LogError("Failed to find feed {feed}", feed);
            return TypedResults.NotFound();
        }

        var requestingDid = BskyAuth.GetDidFromAuthHeader(
            authorization,
            "app.bsky.feed.getFeedSkeleton",
            _config.Identity.ServiceDid
        );

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

        _logger.LogInformation(
            "Fetching feed {feed} with limit {limit} at cursor {cursor} from {requestor}",
            feed,
            limit,
            cursor,
            requestingDid
        );

        try
        {
            var feedSkel = await feedInstance.GetFeedSkeleton(
                requestingDid,
                limit,
                cursor,
                cancellationToken
            );
            return TypedResults.Ok(feedSkel);
        }
        catch (FeedProhibitedException)
        {
            // The PDS expects a 401 in this case
            return TypedResults.Unauthorized();
        }
    }

    public DescribeFeedGeneratorResponse DescribeFeedGenerator()
    {
        return new DescribeFeedGeneratorResponse(
            _config.Identity.ServiceDid,
            _feeds.Keys.Select(s => new DescribeFeedGeneratorFeed(s))
        );
    }

    public async Task<string> GetHydratedFeed(
        HttpResponse res,
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_feeds.TryGetValue(feed, out IFeed? feedInstance))
        {
            _logger.LogError("Failed to find feed {feed}", feed);
            throw new Exception("Failed to find feed");
        }

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

        var feedSkel = await feedInstance.GetFeedSkeleton(
            _session.Did,
            limit,
            cursor,
            cancellationToken
        );
        var feedsInSize = feedSkel.Feed.Take(25).Select(s => new ATUri(s.Post));
        var hydratedRes = await _proto.Feed.GetPostsAsync(feedsInSize, cancellationToken);
        var hydrated = hydratedRes.HandleResult();

        res.ContentType = "application/json";
        return JsonSerializer.Serialize(hydrated, _proto.Options.JsonSerializerOptions);
    }

    public async Task<IResult> Install(CancellationToken cancellationToken = default)
    {
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

        _logger.LogInformation("Installing feeds");

        foreach (var (feedUri, feedCls) in _feeds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var feedName = feedUri.Split("/").Last();

            var record = new CustomFeedRecord(
                _config.Identity.ServiceDid,
                feedCls.DisplayName,
                feedCls.Description,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            );
            var create = new CreateCustomFeedRecord(
                Constants.FeedType.Generator,
                _session.Did.ToString(),
                record,
                feedName
            );

            var recordRefResult = await _proto.Repo.PutRecord(
                create,
                BskySourceGenerationContext.Default.CreateCustomFeedRecord,
                BskySourceGenerationContext.Default.CustomRecordRef,
                cancellationToken: cancellationToken
            );

            var recordRef = recordRefResult.HandleResult();
            _logger.LogDebug(
                "Installed {uri}: {status}",
                recordRef.Uri,
                recordRef.ValidationStatus
            );
        }

        return TypedResults.Ok("Done!");
    }

    private async Task EnsureLogin(CancellationToken cancellationToken = default)
    {
        if (_session == null || !_proto.IsAuthenticated)
        {
            _logger.LogInformation("Logging in");
            _session =
                await _proto.AuthenticateWithPasswordAsync(
                    _config.Auth.Username,
                    _config.Auth.Password,
                    cancellationToken: cancellationToken
                ) ?? throw new Exception("Failed to login");
        }
    }
}

public record DescribeFeedGeneratorFeed(string Uri);

public record DescribeFeedGeneratorResponse(
    string Did,
    IEnumerable<DescribeFeedGeneratorFeed> Feeds
);
