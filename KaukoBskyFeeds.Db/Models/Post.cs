using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(Did), nameof(Rkey))]
[Index(nameof(EventTime))]
public class Post
{
    [Required]
    public required string Did { get; set; }

    [Required]
    public required string Rkey { get; set; }

    [Required]
    public DateTime EventTime { get; set; }

    [Required]
    public long EventTimeUs { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }
    public List<string>? Langs { get; set; }

    [Required]
    [MaxLength(3000)]
    public required string Text { get; set; }

    public string? ReplyParentUri { get; set; }
    public string? ReplyRootUri { get; set; }
    public PostEmbeds? Embeds { get; set; }
}

public class PostEmbeds
{
    public List<PostEmbedImage>? Images { get; set; }
    public string? RecordUri { get; set; }
}

public class PostEmbedImage
{
    public required string RefLink { get; set; }
    public required string MimeType { get; set; }
}
