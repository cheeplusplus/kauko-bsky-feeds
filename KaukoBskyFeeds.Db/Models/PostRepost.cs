using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(RepostDid), nameof(RepostRkey))]
[Index(nameof(ParentDid), nameof(ParentRkey))]
[Index(nameof(EventTime), AllDescending = true)]
public class PostRepost : IPostInteraction
{
    [Required]
    public required string RepostDid { get; set; }

    [Required]
    public required string RepostRkey { get; set; }

    [Required]
    public required string ParentDid { get; set; }

    [Required]
    public required string ParentRkey { get; set; }

    [Required]
    public DateTime EventTime { get; set; }

    [Required]
    public long EventTimeUs { get; set; }

    [NotMapped]
    public PostRecordRef Ref => new(RepostDid, RepostRkey);

    [NotMapped]
    public PostRecordRef ParentRef => new(ParentDid, ParentRkey);
}
