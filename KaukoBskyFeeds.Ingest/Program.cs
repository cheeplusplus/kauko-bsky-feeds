using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Workers;
using KaukoBskyFeeds.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

var bskyDataPath = BskyConfigExtensions.GetDataDir("bsky.config.json");
builder.Configuration.AddJsonFile(bskyDataPath.ConfigPath);
var ingestDataPath = BskyConfigExtensions.GetDataDir("ingest.config.json");
builder.Configuration.AddJsonFile(ingestDataPath.ConfigPath);

builder.Services.AddDbContext<FeedDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("psqldb");
    options.UseNpgsql(connStr);

    // db.EnableSensitiveDataLogging();
    options.ConfigureWarnings(b =>
    {
        b.Log((RelationalEventId.CommandExecuted, LogLevel.Trace));

        // Ignore until https://github.com/dotnet/efcore/issues/35382 is fixed
        b.Ignore(RelationalEventId.PendingModelChangesWarning);
    });
});

// builder.Services.AddScoped<IJetstreamConsumer, JetstreamConsumerNativeWs>();
builder.Services.AddScoped<IJetstreamConsumer, JetstreamConsumerWSC>();
builder.Services.AddHostedService<JetstreamWorker>();

if (builder.Configuration.GetValue<bool>("IngestConfig:Verbose"))
{
    builder.Logging.AddFilter("KaukoBskyFeeds.Ingest.Workers.JetstreamWorker", LogLevel.Debug);
    builder.Logging.AddFilter(
        "KaukoBskyFeeds.Ingest.Jetstream.JetstreamConsumerWSC",
        LogLevel.Debug
    );
}

IHost host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();
