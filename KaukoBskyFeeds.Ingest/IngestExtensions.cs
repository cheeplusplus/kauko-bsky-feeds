using KaukoBskyFeeds.Db.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Shared.Bsky;

namespace KaukoBskyFeeds.Ingest;

public static class IngestExtensions
{
    public static Post? ToDbPost(this JetstreamMessage post)
    {
        if (post.Commit?.Record is not AppBskyFeedPost record)
        {
            return null;
        }

        return new Post
        {
            Did = post.Did,
            Rkey = post.Commit.RecordKey,
            EventTime = post.MessageTime.AsUtc(),
            EventTimeUs = post.TimeMicroseconds,
            CreatedAt = record.CreatedAt.AsUtc(),
            Langs = record.Langs,
            Text = record.Text.Replace("\0", string.Empty),
            ReplyParentUri = record.Reply?.Parent.Uri,
            ReplyRootUri = record.Reply?.Root.Uri,
            EmbedType = record.Embed?.GetRecordType(),
            ImageCount = record.Embed is AppBskyFeedPostEmbedImages emb ? emb.Images.Count : 0,
            EmbedRecordUri = record.Embed switch
            {
                AppBskyFeedPostEmbedRecord rec => rec.Record.Uri,
                AppBskyFeedPostEmbedRecordWithMedia recm => recm.Record?.Uri,
                _ => null,
            },
        };
    }

    public static PostLike? AsDbPostLike(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedLike like)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromAtUri(like.Subject.Uri);
        if (subjectDid == null || subjectRkey == null)
        {
            return null;
        }

        return new PostLike
        {
            LikeDid = message.Did,
            LikeRkey = message.Commit.RecordKey,
            ParentDid = subjectDid,
            ParentRkey = subjectRkey,
            EventTime = message.MessageTime.AsUtc(),
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    public static PostQuotePost? AsDbPostQuotePost(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedPost post)
        {
            return null;
        }

        string? recordUri = null;
        if (post.Embed is IAppBskyFeedPostEmbedWithRecord embedRecord)
        {
            recordUri = embedRecord.Record?.Uri;
        }
        if (recordUri == null)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromAtUri(recordUri);
        if (subjectDid == null || subjectRkey == null)
        {
            return null;
        }

        return new PostQuotePost
        {
            QuoteDid = message.Did,
            QuoteRkey = message.Commit.RecordKey,
            ParentDid = subjectDid,
            ParentRkey = subjectRkey,
            EventTime = message.MessageTime.AsUtc(),
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    public static PostReply? AsDbPostReply(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedPost post || post.Reply == null)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromAtUri(post.Reply.Parent.Uri);
        if (subjectDid == null || subjectRkey == null)
        {
            return null;
        }

        return new PostReply
        {
            ReplyDid = message.Did,
            ReplyRkey = message.Commit.RecordKey,
            ParentDid = subjectDid,
            ParentRkey = subjectRkey,
            EventTime = message.MessageTime.AsUtc(),
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    public static PostRepost? AsDbPostRepost(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedRepost repost)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromAtUri(repost.Subject.Uri);
        if (subjectDid == null || subjectRkey == null)
        {
            return null;
        }

        return new PostRepost
        {
            RepostDid = message.Did,
            RepostRkey = message.Commit.RecordKey,
            ParentDid = subjectDid,
            ParentRkey = subjectRkey,
            EventTime = message.MessageTime.AsUtc(),
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    public static string? GetSubjectDid(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not IAppBskyFeedWithSubject fws)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromAtUri(fws.Subject.Uri);
        if (subjectDid == null || subjectRkey == null)
        {
            return null;
        }

        return subjectDid;
    }

    private static (string?, string?) GetPairFromAtUri(string uri)
    {
        var subjectUri = FishyFlip.Models.ATUri.Create(uri);
        var subjectDid = subjectUri.Did?.ToString();
        var subjectRkey = subjectUri.Rkey;
        if (string.IsNullOrEmpty(subjectDid) || string.IsNullOrEmpty(subjectRkey))
        {
            return (null, null);
        }
        return (subjectDid, subjectRkey);
    }
}
