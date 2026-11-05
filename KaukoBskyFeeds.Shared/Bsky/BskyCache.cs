using System.Collections.Concurrent;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Shared.Bsky;

public class BskyCache(ILogger<BskyCache> logger, IBskyApi api, HybridCache cache) : IBskyCache
{
    public static readonly TimeSpan NormalCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BackgroundCacheDuration = TimeSpan.FromDays(1);

    public static readonly HybridCacheEntryOptions DefaultOpts = new()
    {
        Expiration = NormalCacheDuration,
    };

    public static readonly HybridCacheEntryOptions QuickOpts = new()
    {
        Expiration = TimeSpan.FromSeconds(10),
    };

    public async Task<IEnumerable<PostView?>> GetPosts(
        IEnumerable<ATUri> postUris,
        CancellationToken cancellationToken = default
    )
    {
        var postUrisList = postUris.ToList();
        var posts = await MultipartCache(
            postUrisList,
            async (uris, ct) =>
            {
                var freshPosts = await api.GetPosts(uris.ToList(), ct);
                return freshPosts?.Posts.ToDictionary(k => k.Uri, v => v);
            },
            25,
            k => $"post_{k.AsSafeKey()}",
            k => ["post", $"{k}"],
            cancellationToken
        );

        return postUrisList.Select(s => posts[s]);
    }

    public async Task<IEnumerable<ATDid>> GetFollowing(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        return await BackgroundCache(
            $"user:{user.AsSafeKey()}:following",
            async ct =>
            {
                logger.LogDebug("Fetching user {listUri} following", user);
                return await api.GetFollowsEnumerable(user, ct).ToListAsync(s => s.Did);
            },
            ["user", $"user/{user}", $"user/{user}/following"],
            cancellationToken
        );
    }

    public async Task<IEnumerable<ATDid>> GetFollowers(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        return await BackgroundCache(
            $"user:{user.AsSafeKey()}:followers",
            async ct =>
            {
                logger.LogDebug("Fetching user {listUri} followers", user);
                return await api.GetFollowersEnumerable(user, ct).ToListAsync(s => s.Did);
            },
            ["user", $"user/{user}", $"user/{user}/followers"],
            cancellationToken
        );
    }

    public async Task<IEnumerable<ATDid>> GetMutuals(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        // Fetch both lists in parallel
        var allLists = await Task.WhenAll(
            GetFollowing(user, cancellationToken),
            GetFollowers(user, cancellationToken)
        );
        var followingList = allLists[0].Select(s => s.Handler);
        var followersList = allLists[1].Select(s => s.Handler);

        return followingList.Intersect(followersList).Select(ATDid.Create).WhereNotNull().ToList();
    }

    public async Task<IEnumerable<ATDid>> GetListMembers(
        ATUri listUri,
        CancellationToken cancellationToken = default
    )
    {
        return await BackgroundCache(
            $"list:{listUri.AsSafeKey()}:members",
            async (ct) =>
            {
                logger.LogDebug("Fetching list {listUri} members list", listUri);
                return await api.GetListEnumerable(listUri, ct).ToListAsync(s => s.Subject.Did);
            },
            ["list", $"list/{listUri}", $"user/{listUri.Did}"],
            cancellationToken
        );
    }

    public async Task<Dictionary<ATDid, ProfileViewDetailed?>> GetProfiles(
        IEnumerable<ATDid> users,
        CancellationToken cancellationToken = default
    )
    {
        return await MultipartCache(
            users,
            async (dids, ct) =>
            {
                var freshProfiles = await api.GetProfiles(dids.Cast<ATIdentifier>().ToList(), ct);
                return freshProfiles?.Profiles.ToDictionary(d => d.Did, d => d);
            },
            25,
            k => $"user_{k.AsSafeKey()}_profile",
            k => ["user", $"user/{k}", $"user/{k}/profile"],
            cancellationToken
        );
    }

    // This only gets recent likes
    public async Task<IEnumerable<ATUri>> GetLikes(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        return await BackgroundCache(
            $"user:{user.AsSafeKey()}:likes",
            async ct =>
            {
                logger.LogDebug("Fetching user {user} likes", user);
                var d = await api.RepoListRecords(user, BskyConstants.CollectionTypeLike, ct);
                return d?.Records.Select(s => s.Value as Like)
                        .Select(s => s?.Subject?.Uri)
                        .WhereNotNull() ?? [];
            },
            ["user", $"user/{user}", $"user/{user}/likes"],
            cancellationToken
        );
    }

    public async Task<IEnumerable<ATUri>> GetLists(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        return await BackgroundCache(
            $"user:{user.AsSafeKey()}:lists",
            async ct =>
            {
                logger.LogDebug("Fetching user {user} lists", user);
                return await api.GetListsEnumerable(user, ct).ToListAsync(s => s.Uri);
            },
            ["user", $"user/{user}", $"user/{user}/lists"],
            cancellationToken
        );
    }

    // Cache that supports individual item caching with incomplete/partial fetches
    // Note: this doesn't support background caching, so be careful in a hot path
    // This also doesn't have stampede protection
    private async Task<Dictionary<TIn, TOut?>> MultipartCache<TOut, TIn>(
        IEnumerable<TIn> inputs,
        Func<IEnumerable<TIn>, CancellationToken, ValueTask<Dictionary<TIn, TOut>?>> factory,
        int fetchChunkSize,
        Func<TIn, string> keyFactory,
        Func<TIn, IEnumerable<string>> tagFactory,
        CancellationToken cancellationToken
    )
        where TIn : notnull
        where TOut : class
    {
        api.AssertLogin();

        var inputsList = inputs.ToList();

        // fetch all available cached entries first
        var allValues = new Dictionary<TIn, TOut?>();
        foreach (var inputValue in inputsList)
        {
            allValues[inputValue] = await cache.GetAsync<TOut>(
                $"bsc:{keyFactory(inputValue)}",
                cancellationToken
            );
        }

        // fetch remaining values and cache them
        var valuesToFetch = allValues.Where(w => w.Value == null).Select(s => s.Key).ToList();
        if (valuesToFetch.Count > 0)
        {
            var fetchChunks = valuesToFetch.Chunk(fetchChunkSize);
            foreach (var fetchChunkGroup in fetchChunks)
            {
                var freshValues = await factory(fetchChunkGroup, cancellationToken);
                foreach (var freshValue in freshValues ?? [])
                {
                    if (freshValue.Value == null)
                    {
                        continue;
                    }

                    allValues[freshValue.Key] = freshValue.Value;
                    await cache.SetAsync(
                        $"bsc:{keyFactory(freshValue.Key)}",
                        freshValue.Value,
                        DefaultOpts,
                        tagFactory(freshValue.Key),
                        cancellationToken
                    );
                }
            }
        }

        return allValues;
    }

    // Best-effort at locking
    private readonly ConcurrentDictionary<string, byte> _refetchLocks = new();

    // Cache that returns first and refreshes in the background
    private async Task<T> BackgroundCache<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        IEnumerable<string> tags,
        CancellationToken cancellationToken
    )
    {
        api.AssertLogin();

        var realKey = $"bsc:{key}";
        Func<CancellationToken, ValueTask<BackgroundCacheWrap<T>>> cacheFactory = async (ict) =>
        {
            var fresh = await factory(ict);
            return new BackgroundCacheWrap<T>(fresh, DateTime.UtcNow.Add(NormalCacheDuration));
        };

        var value = await cache.GetAsync<BackgroundCacheWrap<T>>(realKey, cancellationToken);
        if (value != null)
        {
            // If we have a value, return it
            // But if it's a little too old, then refetch in the background too
            if (value.LocalExpiry < DateTime.UtcNow && _refetchLocks.TryAdd(realKey, 0))
            {
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            // Forcibly overwrite the value since it won't have actually expired yet
                            await cache.SetAsync(
                                realKey,
                                await cacheFactory(CancellationToken.None),
                                _backgroundCacheOptions,
                                tags,
                                CancellationToken.None
                            );
                        }
                        finally
                        {
                            _refetchLocks.TryRemove(realKey, out _);
                        }
                    },
                    cancellationToken
                );
            }

            return value.Value;
        }

        // Live fetch
        var fetched = await cache.GetOrCreateAsync(
            realKey,
            cacheFactory,
            _backgroundCacheOptions,
            tags,
            CancellationToken.None
        );
        return fetched.Value;
    }

    private readonly HybridCacheEntryOptions _backgroundCacheOptions = new()
    {
        Expiration = BackgroundCacheDuration,
        LocalCacheExpiration = NormalCacheDuration,
    };

    private record BackgroundCacheWrap<T>(T Value, DateTime LocalExpiry);
}

public static class CacheExtensions
{
    private static readonly HybridCacheEntryOptions GetOnlyOptions = new()
    {
        Flags =
            HybridCacheEntryFlags.DisableDistributedCacheWrite
            | HybridCacheEntryFlags.DisableLocalCacheWrite,
    };

    public static async ValueTask<T?> GetAsync<T>(
        this HybridCache cache,
        string key,
        CancellationToken cancellationToken = default
    )
    {
        return await cache.GetOrCreateAsync(
            key,
            (_) => ValueTask.FromResult(default(T)),
            GetOnlyOptions,
            cancellationToken: cancellationToken
        );
    }
}
