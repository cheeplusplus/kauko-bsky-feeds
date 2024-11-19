using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace KaukoBskyFeeds.Web.Controllers;

[ApiController]
[Route("xrpc")]
public class XrpcController(
    ILogger<XrpcController> logger,
    IConfiguration configuration,
    FeedRegistry feedRegistry,
    ATProtocol proto
) : ControllerBase
{
    private readonly BskyConfigBlock bskyConfig =
        configuration.GetSection("BskyConfig").Get<BskyConfigBlock>()
        ?? throw new Exception("Failed to read configuration");
    private Session? _session;

    [HttpGet("app.bsky.feed.getFeedSkeleton")]
    public async Task<
        Results<NotFound, UnauthorizedHttpResult, Ok<CustomSkeletonFeed>>
    > GetFeedSkeleton(
        [FromServices] IServiceProvider sp,
        [FromHeader] string? authorization,
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var feedInstance = feedRegistry.GetFeedInstance(sp, feed);
        if (feedInstance == null)
        {
            logger.LogError("Failed to find feed {feed}", feed);
            return TypedResults.NotFound();
        }

        var requestingDid = BskyAuth.GetDidFromAuthHeader(
            authorization,
            "app.bsky.feed.getFeedSkeleton",
            bskyConfig.Identity.ServiceDid
        );

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (_session == null)
        {
            throw new Exception("Not logged in!");
        }

        // Handle feed owner restriction
        if (feedInstance.Config.RestrictToFeedOwner && requestingDid != _session?.Did)
        {
            return TypedResults.Unauthorized();
        }

        logger.LogInformation(
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

    [HttpGet("app.bsky.feed.describeFeedGenerator")]
    public DescribeFeedGeneratorResponse DescribeFeedGenerator()
    {
        return new DescribeFeedGeneratorResponse(
            bskyConfig.Identity.ServiceDid,
            feedRegistry.AllFeedUris.Select(s => new DescribeFeedGeneratorFeed(s))
        );
    }

    private async Task EnsureLogin(CancellationToken cancellationToken = default)
    {
        if (_session == null || !proto.IsAuthenticated)
        {
            _session =
                proto.Session
                ?? await proto.AuthenticateWithPasswordAsync(
                    bskyConfig.Auth.Username,
                    bskyConfig.Auth.Password,
                    cancellationToken: cancellationToken
                )
                ?? throw new Exception("Failed to login");
        }
    }

    public record DescribeFeedGeneratorFeed(string Uri);

    public record DescribeFeedGeneratorResponse(
        string Did,
        IEnumerable<DescribeFeedGeneratorFeed> Feeds
    );
}
