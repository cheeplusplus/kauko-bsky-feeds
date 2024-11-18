using System.Data.Common;
using KaukoBskyFeeds.Db.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KaukoBskyFeeds.Db;

public class FeedDbContext(DbContextOptions<FeedDbContext> options) : DbContext(options)
{
    public required DbSet<Post> Posts { get; set; }

    public string DbPath { get; } = "jetstream.db";

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite($"Data Source={DbPath}");
        }
        options.AddInterceptors(new PostUpsertInterceptor());
    }

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

public class PostUpsertInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        ManipulateCommand(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        ManipulateCommand(command);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    private static void ManipulateCommand(DbCommand command)
    {
        if (command.CommandText.StartsWith("INSERT INTO \"Posts\" ", StringComparison.Ordinal))
        {
            command.CommandText = command.CommandText.Replace(
                "INSERT INTO \"Posts\" ",
                "INSERT OR IGNORE INTO \"Posts\" "
            );
        }
    }
}
