using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Shared.Bsky;

public interface IBskyCache
{
    Task<IEnumerable<ATDid>> GetFollowing(
        ATProtocol proto,
        ATDid user,
        CancellationToken cancellationToken = default
    );
    Task<IEnumerable<ATDid>> GetFollowers(
        ATProtocol proto,
        ATDid user,
        CancellationToken cancellationToken = default
    );
    Task<IEnumerable<ATDid>> GetListMembers(
        ATProtocol proto,
        ATUri listUri,
        CancellationToken cancellationToken = default
    );
    Task<FeedProfile?> GetProfile(
        ATProtocol proto,
        ATDid user,
        CancellationToken cancellationToken = default
    );
}

public class BskyCache(ILogger<BskyCache> logger, IMemoryCache cache) : IBskyCache
{
    public static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);
    public static readonly MemoryCacheEntryOptions DEFAULT_OPTS = new()
    {
        AbsoluteExpirationRelativeToNow = CACHE_DURATION,
    };

    public async Task<IEnumerable<ATDid>> GetFollowing(
        ATProtocol proto,
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        return await cache.GetOrCreateAsync(
                $"user_{user}_following",
                async (_) =>
                {
                    logger.LogDebug("Fetching user {listUri} following", user);
                    return (
                        await BskyExtensions.GetAllResults(
                            async (cursor, ct) =>
                            {
                                var r = await proto.Graph.GetFollowsAsync(
                                    proto.Session.Handle,
                                    cursor: cursor,
                                    cancellationToken: ct
                                );
                                var d = r.HandleResult();
                                return (d?.Follows, d?.Cursor);
                            },
                            cancellationToken
                        )
                    ).Select(s => s.Did).ToList();
                },
                DEFAULT_OPTS
            ) ?? [];
    }

    public async Task<IEnumerable<ATDid>> GetFollowers(
        ATProtocol proto,
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        return await cache.GetOrCreateAsync(
                $"user_{user}_followers",
                async (_) =>
                {
                    logger.LogDebug("Fetching user {listUri} followers", user);
                    return (
                        await BskyExtensions.GetAllResults(
                            async (cursor, ct) =>
                            {
                                var r = await proto.Graph.GetFollowersAsync(
                                    proto.Session.Handle,
                                    cursor: cursor,
                                    cancellationToken: ct
                                );
                                var d = r.HandleResult();
                                return (d?.Followers, d?.Cursor);
                            },
                            cancellationToken
                        )
                    ).Select(s => s.Did).ToList();
                },
                DEFAULT_OPTS
            ) ?? [];
    }

    public async Task<IEnumerable<ATDid>> GetListMembers(
        ATProtocol proto,
        ATUri listUri,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        return await cache.GetOrCreateAsync(
                $"list_{listUri}_members",
                async (_) =>
                {
                    logger.LogDebug("Fetching list {listUri} members list", listUri);
                    var listMembers = await BskyExtensions.GetAllResults(
                        async (cursor, ct) =>
                        {
                            var r = await proto.Graph.GetListAsync(
                                listUri,
                                cursor: cursor,
                                cancellationToken: ct
                            );
                            var d = r.HandleResult();
                            return (d?.Items, d?.Cursor);
                        },
                        cancellationToken
                    );

                    var listMemberDids = listMembers
                        .Select(s => s.Subject.Did)
                        .Where(w => w != null)
                        .Cast<ATDid>()
                        .ToList();

                    return listMemberDids;
                },
                DEFAULT_OPTS
            ) ?? [];
    }

    public async Task<FeedProfile?> GetProfile(
        ATProtocol proto,
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        return await cache.GetOrCreateAsync(
            $"user_{user}_profile",
            async (_) =>
            {
                logger.LogDebug("Fetching user {user} profile", user);
                var profileRes = await proto.Actor.GetProfileAsync(user, cancellationToken);
                return profileRes.HandleResult();
            },
            DEFAULT_OPTS
        );
    }
}
