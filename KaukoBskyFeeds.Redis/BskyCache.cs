using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Redis;

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

public class BskyCache(ILogger<BskyCache> logger, HybridCache cache) : IBskyCache
{
    public static readonly TimeSpan LONG_CACHE_DURATION = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);
    public static readonly HybridCacheEntryOptions HYBRID_CACHE_OPTS =
        new() { LocalCacheExpiration = CACHE_DURATION, Expiration = LONG_CACHE_DURATION };

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
                async (cacheEntry) =>
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
                HYBRID_CACHE_OPTS,
                tags: [$"user:{user}"],
                cancellationToken: cancellationToken
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
                async (cacheEntry) =>
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
                HYBRID_CACHE_OPTS,
                tags: [$"user:{user}"],
                cancellationToken: cancellationToken
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
                async (cacheEntry) =>
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
                HYBRID_CACHE_OPTS,
                tags: [$"list:{listUri}"],
                cancellationToken: cancellationToken
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
            async (cacheEntry) =>
            {
                logger.LogDebug("Fetching user {user} profile", user);
                var profileRes = await proto.Actor.GetProfileAsync(user, cancellationToken);
                return profileRes.HandleResult();
            },
            HYBRID_CACHE_OPTS,
            tags: [$"user:{user}"],
            cancellationToken: cancellationToken
        );
    }
}
