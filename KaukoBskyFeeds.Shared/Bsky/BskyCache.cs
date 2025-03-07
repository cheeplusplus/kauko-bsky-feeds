using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Shared.Bsky;

public interface IBskyCache
{
    Task<PostView?> GetPost(
        ATProtocol proto,
        ATUri postUri,
        CancellationToken cancellationToken = default
    );
    Task<IEnumerable<PostView?>> GetPosts(
        ATProtocol proto,
        IEnumerable<ATUri> postUris,
        CancellationToken cancellationToken = default
    );
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
    Task<Dictionary<ATDid, FeedProfile?>> GetProfiles(
        ATProtocol proto,
        IEnumerable<ATDid> users,
        CancellationToken cancellationToken = default
    );
}

public class BskyCache(ILogger<BskyCache> logger, IMemoryCache cache, BskyMetrics bskyMetrics)
    : IBskyCache
{
    public static readonly TimeSpan NORMAL_CACHE_DURATION = TimeSpan.FromMinutes(10);
    public static readonly MemoryCacheEntryOptions DEFAULT_OPTS = new()
    {
        AbsoluteExpirationRelativeToNow = NORMAL_CACHE_DURATION,
    };
    public static readonly MemoryCacheEntryOptions SHORT_OPTS = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
    };
    public static readonly MemoryCacheEntryOptions QUICK_OPTS = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10),
    };

    public async Task<PostView?> GetPost(
        ATProtocol proto,
        ATUri postUri,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        return await cache.GetOrCreateAsync(
            $"post_{postUri}",
            async (_) =>
            {
                logger.LogDebug("Fetching post {postUri} following", postUri);
                var r = await proto
                    .Feed.GetPostsAsync([postUri], cancellationToken)
                    .Record(bskyMetrics, "app.bsky.feed.getPosts1");
                var d = r.HandleResult();
                return d.Posts.FirstOrDefault();
            },
            DEFAULT_OPTS
        );
    }

    public async Task<IEnumerable<PostView?>> GetPosts(
        ATProtocol proto,
        IEnumerable<ATUri> postUris,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        // fetch all available cached entries first
        var allPosts = postUris.ToDictionary(
            k => k.ToString(),
            v => new { ATUri = v, Post = cache.Get<PostView>($"post_{v}") }
        );

        // fetch remaining posts and cache them
        var postsToFetch = allPosts
            .Where(w => w.Value.Post == null)
            .Select(s => s.Value.ATUri)
            .ToList();
        if (postsToFetch.Count > 0)
        {
            var postChunks = postsToFetch.Chunk(25); // max profiles for GetPostsAsync
            foreach (var postGroup in postChunks)
            {
                var freshPostsRes = await proto
                    .Feed.GetPostsAsync(postGroup.ToArray(), cancellationToken)
                    .Record(bskyMetrics, "app.bsky.feed.getPosts");
                var freshPosts = freshPostsRes.HandleResult();

                foreach (var post in freshPosts?.Posts ?? [])
                {
                    allPosts[post.Uri.ToString()] = new
                    {
                        ATUri = post.Uri,
                        Post = (PostView?)post,
                    };
                    cache.Set($"post_{post.Uri}", post, SHORT_OPTS);
                }
            }
        }

        return postUris.Select(s => allPosts[s.ToString()].Post);
    }

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
                                var r = await proto
                                    .Graph.GetFollowsAsync(
                                        proto.Session.Handle,
                                        cursor: cursor,
                                        cancellationToken: ct
                                    )
                                    .Record(bskyMetrics, "app.bsky.graph.getFollows");
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
                                var r = await proto
                                    .Graph.GetFollowersAsync(
                                        proto.Session.Handle,
                                        cursor: cursor,
                                        cancellationToken: ct
                                    )
                                    .Record(bskyMetrics, "app.bsky.graph.getFollowers");
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
                            var r = await proto
                                .Graph.GetListAsync(listUri, cursor: cursor, cancellationToken: ct)
                                .Record(bskyMetrics, "app.bsky.graph.getList");
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
                var profileRes = await proto
                    .Actor.GetProfileAsync(user, cancellationToken)
                    .Record(bskyMetrics, "app.bsky.actor.getProfile");
                return profileRes.HandleResult();
            },
            DEFAULT_OPTS
        );
    }

    public async Task<Dictionary<ATDid, FeedProfile?>> GetProfiles(
        ATProtocol proto,
        IEnumerable<ATDid> users,
        CancellationToken cancellationToken = default
    )
    {
        if (proto?.Session == null)
        {
            throw new NotLoggedInException();
        }

        // fetch all available cached entries first
        var allProfiles = users.ToDictionary(
            k => k.ToString(),
            v => new { Did = v, Profile = cache.Get<FeedProfile>($"user_{v}_profile") }
        );

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
                var freshProfilesRes = await proto
                    .Actor.GetProfilesAsync(profileGroup.ToArray(), cancellationToken)
                    .Record(bskyMetrics, "app.bsky.actor.getProfiles");
                var freshProfiles = freshProfilesRes.HandleResult();

                foreach (var profile in freshProfiles?.Profiles ?? [])
                {
                    allProfiles[profile.Did.ToString()] = new
                    {
                        Did = profile.Did,
                        Profile = (FeedProfile?)profile,
                    };
                    cache.Set($"user_{profile.Did}_profile", profile, DEFAULT_OPTS);
                }
            }
        }

        return allProfiles.ToDictionary(k => k.Value.Did, v => v.Value.Profile);
    }
}
