using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(Did), nameof(Rkey))]
[Index(nameof(EventTime))]
public class Post
{
    [Required]
    public string Did { get; set; } = null!;

    [Required]
    public string Rkey { get; set; } = null!;

    [Required]
    public DateTime EventTime { get; set; }

    [Required]
    public long EventTimeUs { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }
    public List<string>? Langs { get; set; }

    [Required]
    [MaxLength(3000)]
    public string Text { get; set; } = null!;

    public string? ReplyParentUri { get; set; }
    public string? ReplyRootUri { get; set; }
    public PostEmbeds? Embeds { get; set; }

    public string ToUri()
    {
        return $"at://{Did}/app.bsky.feed.post/{Rkey}";
    }
}

public class PostEmbeds
{
    public List<PostEmbedImage> Images { get; set; } = null!;
    public string? RecordUri { get; set; }
}

public class PostEmbedImage
{
    public string RefLink { get; set; } = null!;
    public string MimeType { get; set; } = null!;
}
