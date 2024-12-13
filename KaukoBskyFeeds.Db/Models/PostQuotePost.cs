using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[PrimaryKey(nameof(QuoteDid), nameof(QuoteRkey))]
[Index(nameof(ParentDid), nameof(ParentRkey))]
[Index(nameof(EventTime))]
public class PostQuotePost
{
    [Required]
    public required string QuoteDid { get; set; }

    [Required]
    public required string QuoteRkey { get; set; }

    [Required]
    public required string ParentDid { get; set; }

    [Required]
    public required string ParentRkey { get; set; }

    [Required]
    public DateTime EventTime { get; set; }

    [Required]
    public long EventTimeUs { get; set; }
}
