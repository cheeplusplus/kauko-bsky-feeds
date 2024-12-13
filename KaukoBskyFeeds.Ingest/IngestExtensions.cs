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
            EventTime = post.MessageTime,
            EventTimeUs = post.TimeMicroseconds,
            CreatedAt = record.CreatedAt,
            Langs = record.Langs,
            Text = record.Text,
            ReplyParentUri = record.Reply?.Parent?.Uri,
            ReplyRootUri = record.Reply?.Root?.Uri,
            Embeds = record.Embed?.ToDbPost(),
        };
    }

    public static PostEmbeds? ToDbPost(this AppBskyFeedPostEmbed embed)
    {
        if (embed is AppBskyFeedPostEmbedImages imageEmbed)
        {
            // Image(s)
            return new PostEmbeds
            {
                Images = imageEmbed
                    .Images.Select(s =>
                        // this really shouldn't be possible but it sure happens
                        s.Image.Ref?.Link != null
                            ? new PostEmbedImage
                            {
                                RefLink = s.Image.Ref.Link,
                                MimeType = s.Image.MimeType,
                            }
                            : null
                    )
                    .WhereNotNull()
                    .ToList(),
            };
        }
        else if (embed is AppBskyFeedPostEmbedRecord recordEmbed)
        {
            // Quote
            return new PostEmbeds { RecordUri = recordEmbed.Record.Uri };
        }

        return null;
    }

    public static PostLike? AsDbPostLike(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedLike like)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromATUri(like.Subject.Uri);
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
            EventTime = message.MessageTime,
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
        if (post.Embed is AppBskyFeedPostEmbedWithRecord embedRecord)
        {
            recordUri = embedRecord.Record?.Uri;
        }
        if (recordUri == null)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromATUri(recordUri);
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
            EventTime = message.MessageTime,
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    public static PostReply? AsDbPostReply(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedPost post || post.Reply == null)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromATUri(post.Reply.Parent.Uri);
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
            EventTime = message.MessageTime,
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    public static PostRepost? AsDbPostRepost(this JetstreamMessage message)
    {
        if (message.Commit?.Record is not AppBskyFeedRepost repost)
        {
            return null;
        }

        var (subjectDid, subjectRkey) = GetPairFromATUri(repost.Subject.Uri);
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
            EventTime = message.MessageTime,
            EventTimeUs = message.TimeMicroseconds,
        };
    }

    private static (string?, string?) GetPairFromATUri(string uri)
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
