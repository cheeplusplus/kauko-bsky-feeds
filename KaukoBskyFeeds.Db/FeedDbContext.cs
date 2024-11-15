using KaukoBskyFeeds.Db.Models;
using Microsoft.EntityFrameworkCore;

namespace KaukoBskyFeeds.Db;

public class FeedDbContext(DbContextOptions<FeedDbContext> options) : DbContext(options)
{
    public DbSet<Post> Posts { get; set; }

    public string DbPath { get; } = "jetstream.db";

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder
            .Entity<Post>()
            .OwnsOne(
                p => p.Embeds,
                nav =>
                {
                    nav.ToJson();
                    nav.OwnsMany(o => o.Images);
                }
            );
    }
}
