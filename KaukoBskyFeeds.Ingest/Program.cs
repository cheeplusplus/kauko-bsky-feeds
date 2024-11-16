using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("ingest.config.json", optional: true);

builder.Services.AddDbContext<FeedDbContext>(
    (db) =>
    {
        // db.EnableSensitiveDataLogging();
        db.ConfigureWarnings(b => b.Log((RelationalEventId.CommandExecuted, LogLevel.Trace)));
    }
);

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
