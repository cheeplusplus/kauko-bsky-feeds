using System.Globalization;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.App.Bsky.Feed;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Shared.Bsky;
using ATDid = FishyFlip.Models.ATDid;
using ATUri = FishyFlip.Models.ATUri;
using Post = KaukoBskyFeeds.Db.Models.Post;

namespace KaukoBskyFeeds.Feeds.Utils;

public static class PostExtensions
{
    /// <summary>
    /// Get the latest posts, optionally with a cursor.
    /// </summary>
    /// <param name="postTable">Table to query.</param>
    /// <param name="cursor">Cursor to resume from.</param>
    /// <returns>Queryable</returns>
    public static IQueryable<T> LatestFromCursor<T>(this IQueryable<T> postTable, string? cursor)
        where T : IPostRecord
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

        IQueryable<T> q = postTable.OrderByDescending(o => o.EventTime);
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
            Post or PostQuotePost or PostReply or PostWithInteractions =>
                BskyConstants.COLLECTION_TYPE_POST,
            PostLike => BskyConstants.COLLECTION_TYPE_LIKE,
            PostRepost => BskyConstants.COLLECTION_TYPE_REPOST,
            _ => "?",
        };
    }

    public static string ToUri(this PostRecordRef recordRef, string collectionType)
    {
        return $"at://{recordRef.Did}/{collectionType}/{recordRef.Rkey}";
    }

    public static ATUri ToAtUri(this PostRecordRef recordRef, string collectionType)
    {
        return ATUri.Create(ToUri(recordRef, collectionType));
    }

    public static string ToUri(this IPostRecord post)
    {
        return ToUri(post.Ref, post.ToCollectionType());
    }

    public static ATUri ToAtUri(this IPostRecord post)
    {
        return ATUri.Create(ToUri(post));
    }

    public static string ToParentPostUri(this IPostInteraction repost)
    {
        return $"at://{repost.ParentRef.Did}/{BskyConstants.COLLECTION_TYPE_POST}/{repost.ParentRef.Rkey}";
    }

    public static ATUri ToParentPostAtUri(this IPostInteraction repost)
    {
        return ATUri.Create(ToParentPostUri(repost));
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

    public static Post ToDbPost(this PostView postView)
    {
        if (postView.Record == null)
        {
            throw new Exception("Not a record?");
        }

        var did = postView.Uri.Did?.ToString() ?? throw new Exception("Failed to parse URI");
        var rkey = postView.Uri.Rkey;

        var imageCount = 0;
        if (postView.Embed is ViewImages ie)
        {
            imageCount = ie.Images.Count;
        }
        string? embedRecordUri = null;
        if (postView.Embed is ViewRecordDef { Record: PostView rvep })
        {
            embedRecordUri = rvep.Uri.ToString();
        }
        if (postView.Embed is ViewRecordWithMedia { Record.Record: PostView rmep })
        {
            embedRecordUri = rmep.Uri.ToString();
        }

        var indexedAt = postView.IndexedAt ?? DateTime.UtcNow;

        return new Post
        {
            Did = did,
            Rkey = rkey,
            EventTime = indexedAt,
            EventTimeUs = indexedAt.ToMicroseconds(),
            CreatedAt = postView.PostRecord?.CreatedAt ?? DateTime.MinValue,
            Text = postView.PostRecord?.Text ?? string.Empty,
            ReplyParentUri = null,
            ReplyRootUri = null,
            EmbedType = postView.Embed?.Type,
            ImageCount = imageCount,
            EmbedRecordUri = embedRecordUri,
        };
    }

    public static Post ToDbPost(this FeedViewPost feedPost)
    {
        var post = ToDbPost(feedPost.Post);
        post.ReplyParentUri = (feedPost.Reply?.Parent as PostView)?.Uri.ToString();
        post.ReplyRootUri = (feedPost.Reply?.Root as PostView)?.Uri.ToString();

        return post;
    }
}
