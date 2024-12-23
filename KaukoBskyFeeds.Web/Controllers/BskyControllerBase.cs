using FishyFlip;
using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
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
                ?? await Proto.AuthenticateWithPasswordAsync(
                    BskyConfig.Auth.Username,
                    BskyConfig.Auth.Password,
                    cancellationToken: cancellationToken
                )
                ?? throw new Exception("Failed to login");
        }
    }
}
