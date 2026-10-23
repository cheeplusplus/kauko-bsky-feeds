using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db.Models;

[Index(nameof(FeedUri), nameof(UserDid), nameof(ItemUri))]
public class FeedInteraction
{
    [Key]
    public required Guid Id { get; set; }

    [Required]
    public required DateTime EventTime { get; set; }

    [Required]
    public required string FeedUri { get; set; }

    [Required]
    public required string UserDid { get; set; }

    [Required]
    public required string ItemUri { get; set; }

    [Required]
    public required string Interaction { get; set; }
}
