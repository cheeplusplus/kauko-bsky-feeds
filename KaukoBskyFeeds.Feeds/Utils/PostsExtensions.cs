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

    public static string ToCollectionType(this IPostRecord post)
    {
        return post switch
        {
            Post or PostQuotePost or PostReply => BskyConstants.COLLECTION_TYPE_POST,
            PostLike => BskyConstants.COLLECTION_TYPE_LIKE,
            PostRepost => BskyConstants.COLLECTION_TYPE_REPOST,
            _ => "?",
        };
    }

    public static string ToUri(this IPostRecord post)
    {
        // TODO: This isn't quite correct as it may not actually be a post
        return $"at://{post.Ref.Did}/{post.ToCollectionType()}/{post.Ref.Rkey}";
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

    public static Post ToDbPost(this FishyFlip.Models.FeedViewPost feedPost)
    {
        if (feedPost.Post.Record == null)
        {
            throw new Exception("Not a record?");
        }

        var did = feedPost.Post.Uri.Did?.ToString() ?? throw new Exception("Failed to parse URI");
        var rkey = feedPost.Post.Uri.Rkey;

        var imageCount = 0;
        if (feedPost.Post.Embed is FishyFlip.Models.ImageViewEmbed ie && ie.Images != null)
        {
            imageCount = ie.Images.Length;
        }
        string? embedRecordUri = null;
        if (feedPost.Post.Embed is FishyFlip.Models.RecordViewEmbed rve)
        {
            embedRecordUri = rve.Record.Uri.ToString();
        }
        if (feedPost.Post.Embed is FishyFlip.Models.RecordWithMediaViewEmbed rme)
        {
            embedRecordUri = rme.Record?.Record.Uri.ToString();
        }

        return new Post
        {
            Did = did,
            Rkey = rkey,
            EventTime = feedPost.Post.IndexedAt,
            EventTimeUs = feedPost.Post.IndexedAt.ToMicroseconds(),
            CreatedAt = feedPost.Post.Record.CreatedAt ?? DateTime.MinValue,
            Text = feedPost.Post.Record.Text ?? string.Empty,
            ReplyParentUri = feedPost.Reply?.Parent?.Uri?.ToString(),
            ReplyRootUri = feedPost.Reply?.Root?.Uri?.ToString(),
            EmbedType = feedPost.Post.Embed?.Type,
            ImageCount = imageCount,
            EmbedRecordUri = embedRecordUri,
        };
    }
}
