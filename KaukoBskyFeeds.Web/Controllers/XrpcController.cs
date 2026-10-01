using System.Diagnostics.CodeAnalysis;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Interactions;
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
    IBskyApi cApi
) : BskyControllerBase(configuration, cApi)
{
    [HttpGet(BskyConstants.XrpcGetFeedSkeleton)]
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
            BskyConstants.XrpcGetFeedSkeleton,
            BskyConfig.Identity.ServiceDid
        );

        // Attempt login on first fetch
        var self = await EnsureLogin(cancellationToken);

        // Handle feed owner restriction
        if (feedInstance.Config.RestrictToFeedOwner && Equals(requestingDid, self))
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

    [HttpPost(BskyConstants.XrpcSendInteractions)]
    public async Task<Results<Ok, UnauthorizedHttpResult>> SendInteractions(
        CustomSendInteractions input,
        [FromServices] IInteractionCache interactionCache,
        [FromHeader] string? authorization
    )
    {
        var requestingDid = BskyAuth.GetDidFromAuthHeader(
            authorization,
            BskyConstants.XrpcSendInteractions,
            BskyConfig.Identity.ServiceDid
        );
        if (requestingDid == null)
        {
            return TypedResults.Unauthorized();
        }

        var interactionEvents = new List<FeedInteractionEvent>();

        foreach (var interaction in input.Interactions)
        {
            if (interaction.Event == BskyConstants.InteractionSeen)
            {
                // Don't record seen events
                continue;
            }

            StandardFeedContext? stdFeedCtx;
            try
            {
                stdFeedCtx = StandardFeedContext.Deserialize(interaction.FeedContext);
            }
            catch (Exception ex)
            {
                // This is most likely to happen if we forgot to use it when making a feed
                logger.LogWarning(
                    ex,
                    "Failed to deserialize a StandardFeedContext: {ctx}",
                    interaction.FeedContext
                );
                continue;
            }

            if (stdFeedCtx == null)
            {
                logger.LogWarning(
                    "Failed to deserialize a StandardFeedContext: {ctx}",
                    interaction.FeedContext
                );
                continue;
            }

            // We should be validating the feed and that it has interactions turned on..
            // but since this call is authenticated it's probably not a big deal for now
            var feed = stdFeedCtx.FeedUri;
            if (!ATUri.TryCreate(feed, out var feedAtUri))
            {
                continue;
            }

            logger.LogInformation(
                "{requestingDid} sent interaction to feed {feed}: {interaction}",
                requestingDid,
                feed,
                interaction.Event
            );

            if (interaction.Item == null || interaction.Event == null)
            {
                continue;
            }

            if (!ATUri.TryCreate(interaction.Item, out var itemAtUri))
            {
                continue;
            }

            interactionEvents.Add(
                new FeedInteractionEvent(feedAtUri, requestingDid, itemAtUri, interaction.Event)
            );
        }

        if (interactionEvents.Count > 0)
        {
            await interactionCache.RecordInteractions(interactionEvents);
        }

        return TypedResults.Ok();
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
