using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(RepostDid), nameof(RepostRkey))]
[Index(nameof(ParentDid), nameof(ParentRkey))]
[Index(nameof(EventTime))]
public class PostRepost
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
}
