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
}
