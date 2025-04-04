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

    public static IQueryable<Post> ConcussiveFromCursor(
        this DbSet<Post> postTable,
        IEnumerable<string> dids,
        int limit,
        string? cursor
    )
    {
        var root = postTable.LatestFromCursor(cursor);
        IQueryable<Post>? result = null;
        foreach (var did in dids)
        {
            var sq = root.Where(w => w.Did == did).OrderByDescending(o => o.EventTime).Take(limit);
            if (result == null)
            {
                result = sq;
            }
            else
            {
                result = result.Concat(sq);
            }
        }
        return Enumerable.Empty<Post>().AsQueryable();
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
            Post or PostQuotePost or PostReply or PostWithInteractions =>
                BskyConstants.COLLECTION_TYPE_POST,
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

    public static Post ToDbPost(this FishyFlip.Models.PostView postView)
    {
        if (postView.Record == null)
        {
            throw new Exception("Not a record?");
        }

        var did = postView.Uri.Did?.ToString() ?? throw new Exception("Failed to parse URI");
        var rkey = postView.Uri.Rkey;

        var imageCount = 0;
        if (postView.Embed is FishyFlip.Models.ImageViewEmbed ie && ie.Images != null)
        {
            imageCount = ie.Images.Length;
        }
        string? embedRecordUri = null;
        if (postView.Embed is FishyFlip.Models.RecordViewEmbed rve)
        {
            embedRecordUri = rve.Record.Uri.ToString();
        }
        if (postView.Embed is FishyFlip.Models.RecordWithMediaViewEmbed rme)
        {
            embedRecordUri = rme.Record?.Record.Uri.ToString();
        }

        return new Post
        {
            Did = did,
            Rkey = rkey,
            EventTime = postView.IndexedAt,
            EventTimeUs = postView.IndexedAt.ToMicroseconds(),
            CreatedAt = postView.Record.CreatedAt ?? DateTime.MinValue,
            Text = postView.Record.Text ?? string.Empty,
            ReplyParentUri = null,
            ReplyRootUri = null,
            EmbedType = postView.Embed?.Type,
            ImageCount = imageCount,
            EmbedRecordUri = embedRecordUri,
        };
    }

    public static Post ToDbPost(this FishyFlip.Models.FeedViewPost feedPost)
    {
        var post = ToDbPost(feedPost.Post);
        post.ReplyParentUri = feedPost.Reply?.Parent?.Uri?.ToString();
        post.ReplyRootUri = feedPost.Reply?.Root?.Uri?.ToString();

        return post;
    }
}
