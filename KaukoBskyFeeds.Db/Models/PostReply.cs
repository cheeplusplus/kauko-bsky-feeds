using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(ReplyDid), nameof(ReplyRkey))]
[Index(nameof(ParentDid), nameof(ParentRkey))]
[Index(nameof(EventTime))]
public class PostReply : IPostInteraction
{
    [Required]
    public required string ReplyDid { get; set; }

    [Required]
    public required string ReplyRkey { get; set; }

    [Required]
    public required string ParentDid { get; set; }

    [Required]
    public required string ParentRkey { get; set; }

    [Required]
    public DateTime EventTime { get; set; }

    [Required]
    public long EventTimeUs { get; set; }

    [NotMapped]
    public PostRecordRef Ref => new(ReplyDid, ReplyRkey);

    [NotMapped]
    public PostRecordRef ParentRef => new(ParentDid, ParentRkey);
}
