using System.Diagnostics.CodeAnalysis;
using FishyFlip;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Shared.Bsky;
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
) : BskyControllerBase(configuration, proto)
{
    [HttpGet("app.bsky.feed.getFeedSkeleton")]
    public async Task<
        Results<NotFound, UnauthorizedHttpResult, JsonHttpResult<CustomSkeletonFeed>>
    > GetFeedSkeleton(
        [FromServices] IServiceProvider sp,
        [FromHeader] string? authorization,
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        AddFeedTag(feed);

        var feedInstance = feedRegistry.GetFeedInstance(sp, feed);
        if (feedInstance == null)
        {
            logger.LogError("Failed to find feed {feed}", feed);
            return TypedResults.NotFound();
        }

        var requestingDid = BskyAuth.GetDidFromAuthHeader(
            authorization,
            "app.bsky.feed.getFeedSkeleton",
            BskyConfig.Identity.ServiceDid
        );

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (Session == null)
        {
            throw new Exception("Not logged in!");
        }

        // Handle feed owner restriction
        if (feedInstance.Config.RestrictToFeedOwner && Equals(requestingDid, Session?.Did))
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
            return TypedResults.Json(feedSkel, BskyExtensions.BskyJso);
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
            BskyConfig.Identity.ServiceDid,
            feedRegistry.AllFeedUris.Select(s => new DescribeFeedGeneratorFeed(s))
        );
    }

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global")]
    public record DescribeFeedGeneratorFeed(string Uri);

    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global")]
    public record DescribeFeedGeneratorResponse(
        string Did,
        IEnumerable<DescribeFeedGeneratorFeed> Feeds
    );
}
