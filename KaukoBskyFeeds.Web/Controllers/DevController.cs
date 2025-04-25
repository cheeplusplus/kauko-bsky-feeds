using System.Globalization;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Feeds.Utils;
using KaukoBskyFeeds.Shared.Bsky;
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
    private bool DevEndpointsEnabled =>
        env.IsDevelopment() || (BskyConfig.Web?.EnableDevEndpoints ?? false);

    [HttpGet("previewFeed")]
    public async Task<
        Results<NotFound, Ok<string>, JsonHttpResult<GetPostsOutput>>
    > GetHydratedFeed(
        [FromServices] IServiceProvider sp,
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        AddFeedTag(feed);

        if (!DevEndpointsEnabled)
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

        // 25 is the limit for GetPostsAsync
        var feedsInSize = feedSkel.Feed.Take(25).Select(s => s.Post).ToList();
        if (feedsInSize.Count == 0)
        {
            return TypedResults.Ok("Feed is empty.");
        }
        var hydratedRes = await Proto.Feed.GetPostsAsync(feedsInSize, cancellationToken);
        var hydrated = hydratedRes.HandleResult();

        Response.Headers.Append("X-Bsky-Cursor", feedSkel.Cursor);
        return TypedResults.Json(hydrated, Proto.Options.JsonSerializerOptions);
    }

    [HttpGet("status")]
    public async Task<Ok<StatusResponse>> Status(CancellationToken cancellationToken = default)
    {
        var lastPost = await dbContext
            .Posts.OrderByDescending(o => o.EventTime)
            .FirstOrDefaultAsync(cancellationToken);
        var lastPostLike = await dbContext
            .PostLikes.OrderByDescending(o => o.EventTime)
            .FirstOrDefaultAsync(cancellationToken);
        var lastPostRepost = await dbContext
            .PostReposts.OrderByDescending(o => o.EventTime)
            .FirstOrDefaultAsync(cancellationToken);

        // var totalPosts = await dbContext.Posts.CountAsync(cancellationToken); // too slow
        var totalPosts = await dbContext
            .Database.SqlQuery<int>(
                $"SELECT reltuples::int as estimate FROM pg_class WHERE relname = 'Posts';"
            )
            .ToListAsync(cancellationToken);
        var totalPostCount = totalPosts.FirstOrDefault();

        static string lat(DateTime? et) =>
            (DateTime.UtcNow - et)?.ToString("d'd 'h'h 'm'm 's's'") ?? "N/A";
        var postDistance = lat(lastPost?.EventTime);
        var likeDistance = lat(lastPostLike?.EventTime);
        var repostDistance = lat(lastPostRepost?.EventTime);

        return TypedResults.Ok(
            new StatusResponse(lastPost, totalPostCount, postDistance, likeDistance, repostDistance)
        );
    }

    public record StatusResponse(
        Db.Models.Post? LatestPost,
        int TotalPosts,
        string PostLatency,
        string LikeLatency,
        string RepostLatency
    );

    [HttpGet("query/user")]
    public async Task<Results<NotFound, JsonHttpResult<ProfileViewDetailed>>> QueryUser(
        [FromQuery] string handle,
        CancellationToken cancellationToken = default
    )
    {
        if (!DevEndpointsEnabled)
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
        if (!DevEndpointsEnabled)
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
        var post = postRes.HandleResult()?.Posts.FirstOrDefault();
        return TypedResults.Json(post, Proto.Options.JsonSerializerOptions);
    }

    [HttpGet("query/cached-post")]
    public async Task<Results<NotFound, JsonHttpResult<CachedPostResponse>>> QueryCachedPost(
        [FromQuery] string did,
        [FromQuery] string rkey,
        CancellationToken cancellationToken = default
    )
    {
        if (!DevEndpointsEnabled)
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

    [HttpPost("install")]
    public async Task<Results<NotFound, Ok<string>>> Install(
        CancellationToken cancellationToken = default
    )
    {
        if (!BskyConfig.Web?.EnableInstall ?? false)
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

            var record = new Generator(
                ATDid.Create(BskyConfig.Identity.ServiceDid),
                feed.FeedBaseConfig.DisplayName,
                feed.FeedBaseConfig.Description,
                createdAt: DateTime.UtcNow
            );

            var recordRefResult = await Proto.Repo.PutRecordAsync(
                Session.Did,
                BskyConstants.COLLECTION_TYPE_FEED_GENERATOR,
                feed.FeedShortname,
                record,
                validate: true,
                cancellationToken: cancellationToken
            );

            var recordRef = recordRefResult.HandleResult();
            if (recordRef != null)
            {
                logger.LogDebug(
                    "Installed {uri}: {status}",
                    recordRef.Uri,
                    recordRef.ValidationStatus
                );
            }
            else
            {
                logger.LogError("Failed to install {uri}", feed.FeedShortname);
            }
        }

        return TypedResults.Ok("Done!");
    }
}
