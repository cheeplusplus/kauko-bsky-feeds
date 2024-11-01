using System.Diagnostics.CodeAnalysis;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Bsky;

public delegate Task<(T[]?, string?)> BskyPageableList<T>(
    string? cursor,
    CancellationToken cancellationToken = default
);

public static class BskyExtensions
{
    public static async Task<IEnumerable<T>> GetAllResults<T>(
        BskyPageableList<T> callback,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        string? cursor = null;
        List<T> results = [];

        if (cancellationToken.IsCancellationRequested)
        {
            return results;
        }

        while (true)
        {
            var (paged, curCursor) = await callback(cursor, cancellationToken);
            if (paged != null)
            {
                results.AddRange(paged);
            }

            if (curCursor == null || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            cursor = curCursor;
        }

        return results;
    }
}

public class ATDidComparer : IEqualityComparer<ATDid>
{
    public bool Equals(ATDid? x, ATDid? y)
    {
        return x?.Handler == y?.Handler;
    }

    public int GetHashCode([DisallowNull] ATDid obj)
    {
        return obj.GetHashCode();
    }
}

public class NotLoggedInException : Exception
{
    public NotLoggedInException()
        : base("Not logged in!") { }
}
