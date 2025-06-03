using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace KaukoBskyFeeds.Web.Controllers;

public abstract class BskyControllerBase(IConfiguration configuration, IBskyApi api)
    : ControllerBase
{
    protected IBskyApi Api { get; } = api;

    protected readonly BskyConfigBlock BskyConfig =
        configuration.GetSection("BskyConfig").Get<BskyConfigBlock>()
        ?? throw new Exception("Failed to read configuration");

    protected async Task<ATDid> EnsureLogin(CancellationToken cancellationToken = default)
    {
        return await Api.Login(BskyConfig.Auth, cancellationToken);
    }

    private void AddRequestTag(string tag, string value)
    {
        var tagsFeature = HttpContext.Features.Get<IHttpMetricsTagsFeature>();
        tagsFeature?.Tags.Add(new KeyValuePair<string, object?>(tag, value));
    }

    protected void AddFeedTag(string feedUri)
    {
        this.AddRequestTag(Tags.AtprotoFeedName, Path.GetFileName(feedUri));
    }
}
