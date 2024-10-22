using System.Globalization;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Bsky.Models;
using KaukoBskyFeeds.FeedProcessor.Feeds;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KaukoBskyFeeds.FeedProcessor;

public class FeedProcessor
{
    private readonly ILogger<FeedProcessor> _logger;
    private readonly BskyConfigBlock _config;
    private readonly ATProtocol _proto;
    private Session? _session;
    private readonly Dictionary<string, IFeed> _availableFeeds = [];

    public FeedProcessor(ILoggerFactory loggerFactory, BskyConfigBlock config)
    {
        _logger = loggerFactory.CreateLogger<FeedProcessor>();
        _config = config;

        var protoLogger = loggerFactory.CreateLogger("AtProto");
        _proto = new ATProtocolBuilder()
            .EnableAutoRenewSession(true)
            .WithLogger(protoLogger)
            .Build();

        _availableFeeds.Add(
            // TODO: Figure out how to load this from the config instead of hardcoding it
            $"{_config.Identity.PublishedAtUri}/{Constants.FeedType.Generator}/minusartlist",
            new TimelineMinusList(
                loggerFactory.CreateLogger<TimelineMinusList>(),
                _proto,
                config.FeedProcessors.KaukoMinusArtists
            )
        );
    }

    public async Task<Results<NotFound, UnauthorizedHttpResult, Ok<SkeletonFeed>>> GetFeedSkeleton(
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_availableFeeds.TryGetValue(feed, out IFeed? feedInstance))
        {
            _logger.LogError("Failed to find feed {feed}", feed);
            return TypedResults.NotFound();
        }

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

        _logger.LogInformation(
            "Fetching feed {feed} with limit {limit} at cursor {cursor}",
            feed,
            limit,
            cursor
        );
        var feedSkel = await feedInstance.GetFeedSkeleton(limit, cursor, cancellationToken);
        return TypedResults.Ok(feedSkel);
    }

    public DescribeFeedGeneratorResponse DescribeFeedGenerator()
    {
        return new DescribeFeedGeneratorResponse(
            _config.Identity.ServiceDid,
            _availableFeeds.Keys.Select(s => new DescribeFeedGeneratorFeed(s))
        );
    }

    public async Task<IResult> Install(CancellationToken cancellationToken = default)
    {
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

        _logger.LogInformation("Installing feeds");

        foreach (var (feedUri, feedCls) in _availableFeeds)
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
                SourceGenerationContext.Default.CreateCustomFeedRecord,
                SourceGenerationContext.Default.CustomRecordRef,
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
