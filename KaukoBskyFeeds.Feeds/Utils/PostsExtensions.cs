using System.Globalization;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.EntityFrameworkCore;
using ATDid = FishyFlip.Models.ATDid;
using ATUri = FishyFlip.Models.ATUri;

namespace KaukoBskyFeeds.Feeds.Utils;

public static class PostExtensions
{
    /// <summary>
    /// Get the latest posts, optionally with a cursor.
    /// </summary>
    /// <param name="postTable">Table to query.</param>
    /// <param name="limit">Maximum posts to pull.</param>
    /// <param name="cursor">Cursor to resume from.</param>
    /// <returns>Queryable</returns>
    public static IQueryable<Post> LatestFromCursor(this DbSet<Post> postTable, string? cursor)
    {
        DateTime? cursorPosition = null;

        // Just using this for now
        if (
            cursor != null
            && DateTime.TryParse(cursor, null, DateTimeStyles.RoundtripKind, out var cursorAsDate)
        )
        {
            cursorPosition = cursorAsDate;
        }

        IQueryable<Post> q = postTable.OrderByDescending(o => o.EventTime);
        if (cursorPosition != null)
        {
            q = q.Where(w => w.EventTime < cursorPosition);
        }

        return q;
    }

    public static string GetCursor(this IPostRecord post)
    {
        return AsCursor(post.EventTime);
    }

    public static string AsCursor(this DateTime dt)
    {
        return dt.ToString("o", CultureInfo.InvariantCulture);
    }

    public static string ToUri(this IPostRecord post)
    {
        return $"at://{post.Ref.Did}/{BskyConstants.COLLECTION_TYPE_POST}/{post.Ref.Rkey}";
    }

    public static ATUri ToAtUri(this IPostRecord post)
    {
        return ATUri.Create(ToUri(post));
    }

    public static ATDid GetAuthorDid(this IPostRecord post)
    {
        return ATDid.Create(post.Ref.Did) ?? throw new Exception("Failed to convert DID from Post");
    }

    public static ATDid? GetReplyParentDid(this BasePost post)
    {
        return GetDidFromAtUri(post.ReplyParentUri);
    }

    public static ATDid? GetReplyRootDid(this BasePost post)
    {
        return GetDidFromAtUri(post.ReplyRootUri);
    }

    public static ATDid? GetEmbedRecordDid(this BasePost post)
    {
        return GetDidFromAtUri(post.EmbedRecordUri);
    }

    private static ATUri? GetAtUriFromString(string? uri)
    {
        return uri != null ? ATUri.Create(uri) : null;
    }

    private static ATDid? GetDidFromAtUri(string? uri)
    {
        var atUri = GetAtUriFromString(uri);
        return atUri != null ? ATDid.Create(atUri.Hostname) : null;
    }
}
