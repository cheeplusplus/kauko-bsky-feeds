using System.Text.Json;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Lexicon.Com.Atproto.Identity;
using FishyFlip.Lexicon.Com.Atproto.Repo;
using FishyFlip.Models;
using KaukoBskyFeeds.Shared.Metrics;

namespace KaukoBskyFeeds.Shared.Bsky;

public interface IBskyApi
{
    JsonSerializerOptions JsonSerializerOptions { get; }

    bool IsLoggedIn { get; }
    ATDid? LoggedInUser { get; }

    Task<ATDid> Login(BskyConfigAuth authConfig, CancellationToken cancellationToken = default);
    ATDid AssertLogin();
    Task RefreshSession();

    // app.bsky.actor
    Task<ProfileViewDetailed?> GetProfile(
        ATIdentifier identifier,
        CancellationToken cancellationToken = default
    );
    Task<GetProfilesOutput?> GetProfiles(
        List<ATIdentifier> identifiers,
        CancellationToken cancellationToken = default
    );

    // app.bsky.feed
    Task<GetListFeedOutput?> GetListFeed(
        ATUri listUri,
        int? limit = null,
        CancellationToken cancellationToken = default
    );

    Task<GetPostsOutput?> GetPosts(
        List<ATUri> postUris,
        CancellationToken cancellationToken = default
    );

    Task<GetTimelineOutput?> GetTimeline(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    // app.bsky.graph
    Task<GetFollowsOutput?> GetFollows(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    Task<GetFollowersOutput?> GetFollowers(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    Task<GetListOutput?> GetList(
        ATUri listUri,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    Task<GetListsOutput?> GetLists(
        ATIdentifier identifier,
        CancellationToken cancellationToken = default
    );

    // com.atproto.identity
    Task<ResolveHandleOutput?> ResolveHandle(
        ATHandle handle,
        CancellationToken cancellationToken = default
    );

    // com.atproto.repo
    Task<ListRecordsOutput?> RepoListRecords(
        ATIdentifier identifier,
        string collectionType,
        CancellationToken cancellationToken = default
    );

    Task<PutRecordOutput?> RepoPutRecord(
        ATIdentifier repo,
        string collection,
        string rkey,
        ATObject record,
        bool? validate = null,
        CancellationToken cancellationToken = default
    );
}

// TODO: Move the cursor walkers from BskyCache to here
// It'd be really nice to just say .Take(50) instead of `limit: 50`
public class BskyApi(ATProtocol proto, BskyMetrics metrics) : IBskyApi
{
    public JsonSerializerOptions JsonSerializerOptions => proto.Options.JsonSerializerOptions;

    public bool IsLoggedIn => proto.Session != null;
    public ATDid? LoggedInUser => proto.Session?.Did;

    public async Task<ATDid> Login(
        BskyConfigAuth authConfig,
        CancellationToken cancellationToken = default
    )
    {
        if (proto.IsAuthenticated && proto.Session != null)
        {
            return proto.Session.Did;
        }

        var r = await proto.AuthenticateWithPasswordResultAsync(
            authConfig.Username,
            authConfig.Password,
            cancellationToken: cancellationToken
        );
        var sess = r.HandleResult();
        if (sess == null)
        {
            throw new NotLoggedInException();
        }

        return sess.Did;
    }

    public ATDid AssertLogin()
    {
        if (proto.Session == null)
        {
            throw new NotLoggedInException();
        }

        return proto.Session.Did;
    }

    public async Task RefreshSession()
    {
        await proto.SessionManager.RefreshSessionAsync();
    }

    public async Task<ProfileViewDetailed?> GetProfile(
        ATIdentifier identifier,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Actor.GetProfileAsync(identifier, cancellationToken)
            .Record(metrics, "app.bsky.actor.getProfile");
        return r.HandleResult();
    }

    public async Task<GetProfilesOutput?> GetProfiles(
        List<ATIdentifier> identifiers,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Actor.GetProfilesAsync(identifiers, cancellationToken)
            .Record(metrics, "app.bsky.actor.getProfiles");
        return r.HandleResult();
    }

    public async Task<GetListFeedOutput?> GetListFeed(
        ATUri listUri,
        int? limit = null,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Feed.GetListFeedAsync(listUri, limit, cancellationToken: cancellationToken)
            .Record(metrics, "app.bsky.feed.getListFeed");
        return r.HandleResult();
    }

    public async Task<GetPostsOutput?> GetPosts(
        List<ATUri> postUris,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Feed.GetPostsAsync(postUris, cancellationToken)
            .Record(metrics, "app.bsky.feed.getPosts");
        return r.HandleResult();
    }

    public async Task<GetTimelineOutput?> GetTimeline(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Feed.GetTimelineAsync(
                limit: limit,
                cursor: cursor,
                cancellationToken: cancellationToken
            )
            .Record(metrics, "app.bsky.feed.getTimeline");
        return r.HandleResult();
    }

    public async Task<GetFollowsOutput?> GetFollows(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Graph.GetFollowsAsync(identifier, cursor: cursor, cancellationToken: cancellationToken)
            .Record(metrics, "app.bsky.graph.getFollows");
        return r.HandleResult();
    }

    public async Task<GetFollowersOutput?> GetFollowers(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Graph.GetFollowersAsync(
                identifier,
                cursor: cursor,
                cancellationToken: cancellationToken
            )
            .Record(metrics, "app.bsky.graph.getFollowers");
        return r.HandleResult();
    }

    public async Task<GetListOutput?> GetList(
        ATUri listUri,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Graph.GetListAsync(listUri, cursor: cursor, cancellationToken: cancellationToken)
            .Record(metrics, "app.bsky.graph.getList");
        return r.HandleResult();
    }

    public async Task<GetListsOutput?> GetLists(
        ATIdentifier identifier,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Graph.GetListsAsync(identifier, cancellationToken: cancellationToken)
            .Record(metrics, "app.bsky.graph.getLists");
        return r.HandleResult();
    }

    public async Task<ResolveHandleOutput?> ResolveHandle(
        ATHandle handle,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Identity.ResolveHandleAsync(handle, cancellationToken)
            .Record(metrics, "com.atproto.identity.resolveHandle");
        ;
        return r.HandleResult();
    }

    public async Task<ListRecordsOutput?> RepoListRecords(
        ATIdentifier identifier,
        string collectionType,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Repo.ListRecordsAsync(identifier, collectionType, cancellationToken: cancellationToken)
            .Record(metrics, "com.atproto.repo.listRecords");
        return r.HandleResult();
    }

    public async Task<PutRecordOutput?> RepoPutRecord(
        ATIdentifier repo,
        string collection,
        string rkey,
        ATObject record,
        bool? validate = null,
        CancellationToken cancellationToken = default
    )
    {
        var r = await proto
            .Repo.PutRecordAsync(
                repo,
                collection,
                rkey,
                record,
                validate: validate,
                cancellationToken: cancellationToken
            )
            .Record(metrics, "com.atproto.repo.putRecord");
        return r.HandleResult();
    }
}
