using System.ComponentModel.DataAnnotations.Schema;

namespace KaukoBskyFeeds.Db.Models;

public class PostWithInteractions : Post
{
    public required int LikeCount { get; set; }
    public required int QuotePostCount { get; set; }
    public required int ReplyCount { get; set; }
    public required int RepostCount { get; set; }

    [NotMapped]
    public int TotalInteractions => LikeCount + QuotePostCount + ReplyCount + RepostCount;
}
