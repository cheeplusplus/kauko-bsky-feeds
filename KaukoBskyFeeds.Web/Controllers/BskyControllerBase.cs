using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace KaukoBskyFeeds.Web.Controllers;

public abstract class BskyControllerBase(IConfiguration configuration, ATProtocol _proto)
    : ControllerBase
{
    protected ATProtocol Proto { get; private set; } = _proto;

    protected readonly BskyConfigBlock BskyConfig =
        configuration.GetSection("BskyConfig").Get<BskyConfigBlock>()
        ?? throw new Exception("Failed to read configuration");
    protected Session? Session { get; private set; }

    protected async Task EnsureLogin(CancellationToken cancellationToken = default)
    {
        if (Session == null || !Proto.IsAuthenticated)
        {
            Session =
                Proto.Session
                ?? (
                    await Proto.AuthenticateWithPasswordResultAsync(
                        BskyConfig.Auth.Username,
                        BskyConfig.Auth.Password,
                        cancellationToken: cancellationToken
                    )
                ).HandleResult()
                ?? throw new Exception("Failed to login");
        }
    }

    protected void AddRequestTag(string tag, string value)
    {
        var tagsFeature = HttpContext.Features.Get<IHttpMetricsTagsFeature>();
        tagsFeature?.Tags.Add(new KeyValuePair<string, object?>(tag, value));
    }

    protected void AddFeedTag(string feedUri)
    {
        this.AddRequestTag(Tags.ATPROTO_FEED_NAME, Path.GetFileName(feedUri));
    }
}
