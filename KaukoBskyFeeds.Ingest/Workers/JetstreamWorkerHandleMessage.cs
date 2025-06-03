using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Ingest.Jetstream.Models;
using KaukoBskyFeeds.Ingest.Jetstream.Models.Records;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Ingest.Workers;

public partial class JetstreamWorker
{
    private async Task HandleMessage(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        metrics.IngestEvent(message.Commit?.Collection ?? "_unknown_", message.MessageTime);

        switch (message.Commit?.Collection)
        {
            case BskyConstants.CollectionTypePost:
                await HandleMessage_Post(message, cancellationToken);
                break;
            case BskyConstants.CollectionTypeLike:
                await HandleMessage_Like(message, cancellationToken);
                break;
            case BskyConstants.CollectionTypeRepost:
                await HandleMessage_Repost(message, cancellationToken);
                break;
        }
    }

    private async Task HandleMessage_Post(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        if (message.Commit?.Operation == null)
        {
            return;
        }

        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            if (wantedDids != null && !wantedDids.Contains(message.Did))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch wanted DIDs list, continuing");
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            var post = message.ToDbPost();
            if (post != null)
            {
                var reply = message.AsDbPostReply();
                var quotePost = message.AsDbPostQuotePost();

                _insertHolder.Add(post, reply, quotePost);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            // Delete everything related to this post
            _insertHolder.DeletePost(message.Did, message.Commit.RecordKey);
        }
        else
        {
            // TODO: Support updates
            logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task HandleMessage_Like(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        if (message.Commit?.Record is not AppBskyFeedLike)
        {
            return;
        }

        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            var subjectDid = message.GetSubjectDid();
            if (
                wantedDids != null
                && !(
                    wantedDids.Contains(message.Did)
                    || (subjectDid != null && wantedDids.Contains(subjectDid))
                )
            )
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch wanted DIDs list, continuing");
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            var like = message.AsDbPostLike();
            if (like != null)
            {
                _insertHolder.Add(like);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            _insertHolder.Delete(
                new PostRecordRef(message.Did, message.Commit.RecordKey),
                d =>
                    d.PostLikes.Where(w =>
                        w.LikeDid == message.Did && w.LikeRkey == message.Commit.RecordKey
                    )
            );
        }
        else
        {
            // TODO: Support updates
            logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }

    private async Task HandleMessage_Repost(
        JetstreamMessage message,
        CancellationToken cancellationToken = default
    )
    {
        if (message.Commit?.Record is not AppBskyFeedRepost)
        {
            return;
        }

        try
        {
            var wantedDids = await FetchWantedDids(message.Commit.Collection, cancellationToken);
            var subjectDid = message.GetSubjectDid();
            if (
                wantedDids != null
                && !(
                    wantedDids.Contains(message.Did)
                    || (subjectDid != null && wantedDids.Contains(subjectDid))
                )
            )
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch wanted DIDs list, continuing");
        }

        if (message.Commit.Operation == JetstreamOperation.Create)
        {
            var repost = message.AsDbPostRepost();
            if (repost != null)
            {
                _insertHolder.Add(repost);
            }
        }
        else if (message.Commit.Operation == JetstreamOperation.Delete)
        {
            _insertHolder.Delete(
                new PostRecordRef(message.Did, message.Commit.RecordKey),
                d =>
                    d.PostReposts.Where(w =>
                        w.RepostDid == message.Did && w.RepostRkey == message.Commit.RecordKey
                    )
            );
        }
        else
        {
            // TODO: Support updates
            logger.LogTrace(
                "Unsupported message operation {op} on {postUri}",
                message.Commit.Operation,
                message.ToAtUri()
            );
        }
    }
}