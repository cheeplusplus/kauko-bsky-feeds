using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Ingest;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Ingest.Workers;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
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
        options =>
        {
            options.CommandTimeout(300);
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

builder.Services.AddBskyServices();

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
                BskyMetrics.METRIC_METER_NAME,
                IngestMetrics.METRIC_METER_NAME,
                JetstreamMetrics.METRIC_METER_NAME
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

IHost host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();
