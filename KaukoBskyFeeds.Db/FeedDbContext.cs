using KaukoBskyFeeds.Db.Models;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db;

public class FeedDbContext(DbContextOptions<FeedDbContext> options) : DbContext(options)
{
    public required DbSet<Post> Posts { get; set; }
    public required DbSet<PostLike> PostLikes { get; set; }
    public required DbSet<PostQuotePost> PostQuotePosts { get; set; }
    public required DbSet<PostReply> PostReplies { get; set; }
    public required DbSet<PostRepost> PostReposts { get; set; }
    public required DbSet<PostWithInteractions> PostsWithInteractions { get; set; }

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder
            .Entity<PostWithInteractions>()
            .HasBaseType((Type?)null)
            .ToView("PostsWithInteractions");
    }
}
