using System.Text.Json;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Lexicon.Com.Atproto.Identity;
using FishyFlip.Lexicon.Com.Atproto.Repo;
using FishyFlip.Models;

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
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<FeedViewPost> GetListFeedEnumerable(
        ATUri listUri,
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

    IAsyncEnumerable<FeedViewPost> GetTimelineEnumerable(
        CancellationToken cancellationToken = default
    );

    // app.bsky.graph
    Task<GetFollowsOutput?> GetFollows(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<ProfileView> GetFollowsEnumerable(
        ATIdentifier identifier,
        CancellationToken cancellationToken = default
    );

    Task<GetFollowersOutput?> GetFollowers(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<ProfileView> GetFollowersEnumerable(
        ATIdentifier identifier,
        CancellationToken cancellationToken = default
    );

    Task<GetListOutput?> GetList(
        ATUri listUri,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<ListItemView> GetListEnumerable(
        ATUri listUri,
        CancellationToken cancellationToken = default
    );

    Task<GetListsOutput?> GetLists(
        ATIdentifier identifier,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<ListView> GetListsEnumerable(
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
