using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.FeedProcessor.Feeds;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

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

        _proto = new ATProtocolBuilder().EnableAutoRenewSession(true).Build();
        _availableFeeds.Add(
            $"{_config.Identity.PublishedAtUri}/{Constants.FeedType.Generator}/kaukominusartists",
            new TimelineMinusList(
                loggerFactory.CreateLogger<TimelineMinusList>(),
                _proto,
                config.FeedProcessors.KaukoMinusArtists
            )
        );
    }

    public async Task<Results<NotFound, UnauthorizedHttpResult, Ok<SkeletonFeed>>> GetFeedSkeleton(
        string feed,
        int limit = 50,
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

    public async Task Install(CancellationToken cancellationToken = default)
    {
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

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
            await _proto.Repo.PutRecord(
                create,
                JsonTypeInfo.CreateJsonTypeInfo<CreateCustomFeedRecord>(
                    _proto.Options.JsonSerializerOptions
                ),
                JsonTypeInfo.CreateJsonTypeInfo<RecordRef>(_proto.Options.JsonSerializerOptions),
                cancellationToken: cancellationToken
            );
        }
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

public record CustomFeedRecord(
    string Did,
    string DisplayName,
    string Description,
    string CreatedAt,
    BlobRecord? Avatar = null
);

public record CreateCustomFeedRecord(
    string Collection,
    string Repo,
    CustomFeedRecord Record,
    string Rkey,
    bool Validate = true
);
