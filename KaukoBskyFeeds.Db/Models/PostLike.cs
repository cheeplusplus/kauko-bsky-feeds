using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(LikeDid), nameof(LikeRkey))]
[Index(nameof(ParentDid), nameof(ParentRkey))]
[Index(nameof(EventTime))]
public class PostLike
{
    [Required]
    public required string LikeDid { get; set; }

    [Required]
    public required string LikeRkey { get; set; }

    [Required]
    public required string ParentDid { get; set; }

    [Required]
    public required string ParentRkey { get; set; }

    [Required]
    public DateTime EventTime { get; set; }

    [Required]
    public long EventTimeUs { get; set; }
}
