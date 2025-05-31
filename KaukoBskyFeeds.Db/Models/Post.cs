using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

public abstract class BasePost : IPostRecord
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

    public string? EmbedType { get; set; }

    [DefaultValue(0)]
    public int ImageCount { get; set; }

    public string? EmbedRecordUri { get; set; }

    [NotMapped]
    public PostRecordRef Ref => new(Did, Rkey);
}

[PrimaryKey(nameof(Did), nameof(Rkey))]
[Index(nameof(EventTime), AllDescending = true)]
[Index(nameof(Did), nameof(EventTime), IsDescending = [false, true])]
public class Post : BasePost { }

public class PostEmbeds
{
    [NotMapped]
    public List<PostEmbedImage>? Images { get; set; }
    public string? RecordUri { get; set; }
}

public class PostEmbedImage
{
    public required string RefLink { get; set; }
    public required string MimeType { get; set; }
}
