using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Shared.Bsky;

public interface IBskyCache
{
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

    Task<Dictionary<ATDid, ProfileViewDetailed?>> GetProfiles(
        IEnumerable<ATDid> users,
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<ATUri>> GetLikes(ATDid user, CancellationToken cancellationToken = default);

    Task<IEnumerable<ATUri>> GetLists(ATDid user, CancellationToken cancellationToken = default);
}
