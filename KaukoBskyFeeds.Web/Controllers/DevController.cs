using System.Globalization;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared.Bsky.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Web.Controllers;

[ApiController]
[Route("dev")]
public class DevController(
    ILogger<DevController> logger,
    IWebHostEnvironment env,
    IConfiguration configuration,
    FeedRegistry feedRegistry,
    FeedDbContext dbContext,
    ATProtocol _proto
) : BskyControllerBase(configuration, _proto)
{
    [HttpGet("previewFeed")]
    public async Task<
        Results<NotFound, Ok<string>, JsonHttpResult<PostCollection>>
    > GetHydratedFeed(
        [FromServices] IServiceProvider sp,
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!env.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        var feedInstance = feedRegistry.GetFeedInstance(sp, feed);
        if (feedInstance == null)
        {
            logger.LogError("Failed to find feed {feed}", feed);
            return TypedResults.NotFound();
        }

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (Session == null)
        {
            throw new Exception("Not logged in!");
        }

        logger.LogInformation(
            "Fetching feed {feed} with limit {limit} at cursor {cursor}",
            feed,
            limit,
            cursor
        );

        var feedSkel = await feedInstance.GetFeedSkeleton(
            Session.Did,
            limit,
            cursor,
            cancellationToken
        );

        var feedsInSize = feedSkel.Feed.Take(25).Select(s => new ATUri(s.Post));
        if (!feedsInSize.Any())
        {
            return TypedResults.Ok("Feed is empty.");
        }
        var hydratedRes = await Proto.Feed.GetPostsAsync(feedsInSize, cancellationToken);
        var hydrated = hydratedRes.HandleResult();

        Response.Headers.Append("X-Bsky-Cursor", feedSkel.Cursor);
        return TypedResults.Json(hydrated, Proto.Options.JsonSerializerOptions);
    }

    [HttpGet("query/user")]
    public async Task<Results<NotFound, JsonHttpResult<FeedProfile>>> QueryUser(
        [FromQuery] string handle,
        CancellationToken cancellationToken = default
    )
    {
        if (!env.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (Session == null)
        {
            throw new Exception("Not logged in!");
        }

        var handled = ATHandle.Create(handle) ?? throw new Exception("Failed to create handle");
        var resolvedDidRes = await Proto.Identity.ResolveHandleAsync(handled, cancellationToken);
        var resolvedDid = resolvedDidRes.HandleResult();
        if (resolvedDid?.Did == null)
        {
            throw new Exception("Failed to resolve DID");
        }
        var profileRes = await Proto.Actor.GetProfileAsync(resolvedDid.Did, cancellationToken);
        var profile = profileRes.HandleResult();
        return TypedResults.Json(profile, Proto.Options.JsonSerializerOptions);
    }

    [HttpGet("query/post")]
    public async Task<Results<NotFound, JsonHttpResult<PostView>>> QueryPost(
        [FromQuery] string atUri,
        CancellationToken cancellationToken = default
    )
    {
        if (!env.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        // Attempt login on first fetch
        await EnsureLogin(cancellationToken);
        if (Session == null)
        {
            throw new Exception("Not logged in!");
        }

        var postRes = await Proto.Feed.GetPostsAsync([ATUri.Create(atUri)], cancellationToken);
        var post = postRes.HandleResult().Posts.FirstOrDefault();
        return TypedResults.Json(post, Proto.Options.JsonSerializerOptions);
    }

    [HttpGet("query/cached-post")]
    public async Task<Results<NotFound, JsonHttpResult<CachedPostResponse>>> QueryCachedPost(
        [FromQuery] string did,
        [FromQuery] string rkey,
        CancellationToken cancellationToken = default
    )
    {
        if (!env.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        var post = await dbContext.PostsWithInteractions.SingleOrDefaultAsync(
            s => s.Did == did && s.Rkey == rkey,
            cancellationToken
        );
        if (post == null)
        {
            return TypedResults.NotFound();
        }

        var atUri = post.ToAtUri().ToString();
        var resp = new CachedPostResponse(atUri, post, post.TotalInteractions);

        return TypedResults.Json(resp);
    }

    public record CachedPostResponse(string AtUri, IPostRecord Post, long TotalInteractions);

    [HttpGet("install")]
    public async Task<Results<NotFound, Ok<string>>> Install(
        CancellationToken cancellationToken = default
    )
    {
        if (!BskyConfig.EnableInstall)
        {
            return TypedResults.NotFound();
        }

        await EnsureLogin(cancellationToken);
        if (Session == null)
        {
            throw new Exception("Not logged in!");
        }

        logger.LogInformation("Installing feeds");

        foreach (var feed in feedRegistry.AllFeeds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            if (!feed.FeedBaseConfig.Install)
            {
                continue;
            }

            var record = new CustomFeedRecord(
                BskyConfig.Identity.ServiceDid,
                feed.FeedBaseConfig.DisplayName,
                feed.FeedBaseConfig.Description,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            );
            var create = new CreateCustomFeedRecord(
                Constants.FeedType.Generator,
                Session.Did.ToString(),
                record,
                feed.FeedShortname
            );

            var recordRefResult = await Proto.Repo.PutRecord(
                create,
                BskySourceGenerationContext.Default.CreateCustomFeedRecord,
                BskySourceGenerationContext.Default.CustomRecordRef,
                cancellationToken: cancellationToken
            );

            var recordRef = recordRefResult.HandleResult();
            logger.LogDebug("Installed {uri}: {status}", recordRef.Uri, recordRef.ValidationStatus);
        }

        return TypedResults.Ok("Done!");
    }
}
