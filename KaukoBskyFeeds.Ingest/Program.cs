using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Workers;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using KaukoBskyFeeds.Shared.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = Host.CreateApplicationBuilder(args);

var bskyDataPath = BskyConfigExtensions.GetDataDir("bsky.config.json");
builder.Configuration.AddJsonFile(bskyDataPath.ConfigPath);
var ingestDataPath = BskyConfigExtensions.GetDataDir("ingest.config.json");
builder.Configuration.AddJsonFile(ingestDataPath.ConfigPath);

builder.Services.AddDbContext<FeedDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("psqldb");
    options.UseNpgsql(
        connStr,
        opts =>
        {
            opts.CommandTimeout(300);
        }
    );

    // db.EnableSensitiveDataLogging();
    options.ConfigureWarnings(b =>
    {
        b.Log((RelationalEventId.CommandExecuted, LogLevel.Trace));

        // Ignore until https://github.com/dotnet/efcore/issues/35382 is fixed
        b.Ignore(RelationalEventId.PendingModelChangesWarning);
    });
});

builder.Services.AddBskyServices(builder.Configuration.GetConnectionString("redis"));

// builder.Services.AddScoped<IJetstreamConsumer, JetstreamConsumerNativeWs>();
builder.Services.AddScoped<IJetstreamConsumer, JetstreamConsumerWSC>();
builder.Services.AddHostedService<JetstreamWorker>();

builder.Services.AddSingleton<BskyMetrics>();
builder.Services.AddSingleton<IngestMetrics>();
builder.Services.AddSingleton<JetstreamMetrics>();
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(b =>
        b.AddService("KaukoBskyFeeds.Ingest", serviceNamespace: "KaukoBskyFeeds")
    )
    .WithMetrics(b =>
        b.AddPrometheusHttpListener(options =>
            {
                var uriPrefix = builder.Configuration.GetValue<string>(
                    "IngestConfig:MetricsUriPrefix"
                );
                if (uriPrefix != null)
                {
                    options.UriPrefixes = [uriPrefix];
                }
            })
            .AddMeter(
                "System.Net.Http",
                "System.Runtime",
                "Microsoft.EntityFrameworkCore",
                BskyMetrics.MetricMeterName,
                IngestMetrics.MetricMeterName,
                JetstreamMetrics.MetricMeterName
            )
    );

if (builder.Configuration.GetValue<bool>("IngestConfig:Verbose"))
{
    builder.Logging.AddFilter("KaukoBskyFeeds.Ingest.Workers.JetstreamWorker", LogLevel.Debug);
    builder.Logging.AddFilter(
        "KaukoBskyFeeds.Ingest.Jetstream.JetstreamConsumerWSC",
        LogLevel.Debug
    );
}

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();
