using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Shared.Bsky;

public interface IBskyCache
{
    Task<PostView?> GetPost(ATUri postUri, CancellationToken cancellationToken = default);

    Task<IEnumerable<PostView?>> GetPosts(
        IEnumerable<ATUri> postUris,
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<ATDid>> GetFollowing(
        ATDid user,
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<ATDid>> GetFollowers(
        ATDid user,
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<ATDid>> GetMutuals(ATDid user, CancellationToken cancellationToken = default);

    Task<IEnumerable<ATDid>> GetListMembers(
        ATUri listUri,
        CancellationToken cancellationToken = default
    );

    Task<ProfileViewDetailed?> GetProfile(
        ATDid user,
        CancellationToken cancellationToken = default
    );

    Task<Dictionary<ATDid, ProfileViewDetailed?>> GetProfiles(
        IEnumerable<ATDid> users,
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<ATUri>> GetLikes(ATDid user, CancellationToken cancellationToken = default);

    Task<IEnumerable<ATUri>> GetLists(ATDid user, CancellationToken cancellationToken = default);
}

public class BskyCache(ILogger<BskyCache> logger, IBskyApi api, HybridCache cache) : IBskyCache
{
    public static readonly TimeSpan NormalCacheDuration = TimeSpan.FromMinutes(10);

    public static readonly HybridCacheEntryOptions DefaultOpts = new()
    {
        Expiration = NormalCacheDuration,
    };

    public static readonly HybridCacheEntryOptions QuickOpts = new()
    {
        Expiration = TimeSpan.FromSeconds(10),
    };

    public async Task<PostView?> GetPost(
        ATUri postUri,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        return await cache.GetOrCreateAsync(
            $"post_{postUri}",
            async (ct) =>
            {
                logger.LogDebug("Fetching post {postUri} following", postUri);
                var d = await api.GetPosts([postUri], ct);
                return d?.Posts.FirstOrDefault();
            },
            DefaultOpts,
            tags: ["post", $"{postUri}", $"user/{postUri.Did}"],
            cancellationToken: cancellationToken
        );
    }

    private record GetPostsDict(ATUri AtUri, PostView? Post);

    public async Task<IEnumerable<PostView?>> GetPosts(
        IEnumerable<ATUri> postUris,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        var postUriList = postUris.ToList();

        // fetch all available cached entries first
        var allPosts = new Dictionary<ATUri, GetPostsDict>();
        foreach (var uri in postUriList)
        {
            allPosts[uri] = new GetPostsDict(
                uri,
                await cache.GetAsync<PostView>($"post_{uri}", cancellationToken)
            );
        }

        // fetch remaining posts and cache them
        var postsToFetch = allPosts
            .Where(w => w.Value.Post == null)
            .Select(s => s.Value.AtUri)
            .ToList();
        if (postsToFetch.Count > 0)
        {
            var postChunks = postsToFetch.Chunk(25); // max profiles for GetPostsAsync
            foreach (var postGroup in postChunks)
            {
                var freshPosts = await api.GetPosts(postGroup.ToList(), cancellationToken);
                foreach (var post in freshPosts?.Posts ?? [])
                {
                    allPosts[post.Uri] = new GetPostsDict(post.Uri, post);
                    await cache.SetAsync(
                        $"post_{post.Uri}",
                        post,
                        DefaultOpts,
                        tags: ["post", $"{post.Uri}"],
                        cancellationToken: cancellationToken
                    );
                }
            }
        }

        return postUriList.Select(s => allPosts[s].Post);
    }

    public async Task<IEnumerable<ATDid>> GetFollowing(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        var self = api.AssertLogin();

        return await cache.GetOrCreateAsync(
            $"user_{user}_following",
            async (ct) =>
            {
                logger.LogDebug("Fetching user {listUri} following", user);
                return await BskyExtensions.GetAllResults(
                    async (cursor, ict) =>
                    {
                        var d = await api.GetFollows(self, cursor, ict);
                        return (d?.Follows.Select(s => s.Did), d?.Cursor);
                    },
                    ct
                );
            },
            DefaultOpts,
            tags: ["user", $"user/{user}"],
            cancellationToken: cancellationToken
        );
    }

    public async Task<IEnumerable<ATDid>> GetFollowers(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        var self = api.AssertLogin();

        return await cache.GetOrCreateAsync(
            $"user_{user}_followers",
            async (ct) =>
            {
                logger.LogDebug("Fetching user {listUri} followers", user);
                return (
                    await BskyExtensions.GetAllResults(
                        async (cursor, ict) =>
                        {
                            var d = await api.GetFollowers(self, cursor, ict);
                            return (d?.Followers.Select(s => s.Did), d?.Cursor);
                        },
                        ct
                    )
                );
            },
            DefaultOpts,
            tags: ["user", $"user/{user}", $"user/{user}/followers"],
            cancellationToken: cancellationToken
        );
    }

    public async Task<IEnumerable<ATDid>> GetMutuals(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        var followingList = await GetFollowing(user, cancellationToken);
        var followersList = await GetFollowers(user, cancellationToken);

        return followingList
            .Select(s => s.Handler)
            .Intersect(followersList.Select(s => s.Handler))
            .Select(ATDid.Create)
            .Where(w => w != null)
            .Cast<ATDid>()
            .ToList();
    }

    public async Task<IEnumerable<ATDid>> GetListMembers(
        ATUri listUri,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        return await cache.GetOrCreateAsync(
            $"list_{listUri}_members",
            async (ct) =>
            {
                logger.LogDebug("Fetching list {listUri} members list", listUri);
                return await BskyExtensions.GetAllResults(
                    async (cursor, ict) =>
                    {
                        var d = await api.GetList(listUri, cursor, ict);
                        return (d?.Items.Select(s => s.Subject.Did), d?.Cursor);
                    },
                    ct
                );
            },
            DefaultOpts,
            tags: ["list", $"{listUri}", $"user/{listUri.Did}"],
            cancellationToken: cancellationToken
        );
    }

    public async Task<ProfileViewDetailed?> GetProfile(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        return await cache.GetOrCreateAsync(
            $"user_{user}_profile",
            async (ct) =>
            {
                logger.LogDebug("Fetching user {user} profile", user);
                return await api.GetProfile(user, ct);
            },
            DefaultOpts,
            tags: ["user", $"user/{user}", $"user/{user}/profile"],
            cancellationToken: cancellationToken
        );
    }

    private record GetProfilesDict(ATDid Did, ProfileViewDetailed? Profile);

    public async Task<Dictionary<ATDid, ProfileViewDetailed?>> GetProfiles(
        IEnumerable<ATDid> users,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        // fetch all available cached entries first
        var allProfiles = new Dictionary<ATDid, GetProfilesDict>();
        foreach (var user in users)
        {
            allProfiles[user] = new GetProfilesDict(
                user,
                await cache.GetAsync<ProfileViewDetailed>($"user_{user}_profile", cancellationToken)
            );
        }

        // fetch remaining profiles and cache them
        var profilesToFetch = allProfiles
            .Where(w => w.Value.Profile == null)
            .Select(s => s.Value.Did)
            .ToList();
        if (profilesToFetch.Count > 0)
        {
            var profileChunks = profilesToFetch.Chunk(25); // max profiles for GetProfilesAsync
            foreach (var profileGroup in profileChunks)
            {
                var freshProfiles = await api.GetProfiles(
                    profileGroup.Cast<ATIdentifier>().ToList(),
                    cancellationToken
                );

                foreach (var profile in freshProfiles?.Profiles ?? [])
                {
                    allProfiles[profile.Did] = new GetProfilesDict(profile.Did, profile);
                    await cache.SetAsync(
                        $"user_{profile.Did}_profile",
                        profile,
                        DefaultOpts,
                        tags: ["user", $"user/{profile.Did}", $"user/{profile.Did}/profile"],
                        cancellationToken: cancellationToken
                    );
                }
            }
        }

        return allProfiles.ToDictionary(k => k.Value.Did, v => v.Value.Profile);
    }

    // This only gets recent likes
    public async Task<IEnumerable<ATUri>> GetLikes(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        return await cache.GetOrCreateAsync(
                $"user_{user}_likes",
                async (ct) =>
                {
                    logger.LogDebug("Fetching user {user} likes", user);
                    var d = await api.RepoListRecords(user, BskyConstants.CollectionTypeLike, ct);
                    return d
                        ?.Records.Select(s => s.Value as Like)
                        .Select(s => s?.Subject?.Uri)
                        .WhereNotNull();
                },
                DefaultOpts,
                tags: ["user", $"user/{user}", $"user/{user}/likes"],
                cancellationToken: cancellationToken
            ) ?? [];
    }

    public async Task<IEnumerable<ATUri>> GetLists(
        ATDid user,
        CancellationToken cancellationToken = default
    )
    {
        api.AssertLogin();

        return await cache.GetOrCreateAsync(
                $"user_{user}_lists",
                async (ct) =>
                {
                    logger.LogDebug("Fetching user {user} lists", user);
                    var d = await api.GetLists(user, ct);
                    return d?.Lists.Select(s => s.Uri);
                },
                DefaultOpts,
                tags: ["user", $"user/{user}", $"user/{user}/lists"],
                cancellationToken: cancellationToken
            ) ?? [];
    }
}

public static class CacheExtensions
{
    public static async ValueTask<T?> GetAsync<T>(
        this HybridCache cache,
        string key,
        CancellationToken cancellationToken = default
    )
    {
        return await cache.GetOrCreateAsync(
            key,
            (_) => ValueTask.FromResult(default(T)),
            new HybridCacheEntryOptions()
            {
                Flags =
                    HybridCacheEntryFlags.DisableDistributedCacheWrite
                    & HybridCacheEntryFlags.DisableLocalCacheWrite,
            },
            cancellationToken: cancellationToken
        );
    }
}
