using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Shared.Metrics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

var dataPath = BskyConfigExtensions.GetDataDir("bsky.config.json");
builder.Configuration.AddJsonFile(dataPath.ConfigPath);

builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        | System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
);

builder.Services.AddDbContext<FeedDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("psqldb");
    options.UseNpgsql(
        connStr,
        options =>
        {
            options.CommandTimeout(60);
        }
    );
});
builder.Services.AddBskyServices(builder.Configuration.GetConnectionString("redis"));
builder.Services.AddSingleton<FeedRegistry>();

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<BskyMetrics>();
builder
    .Services.AddOpenTelemetry()
    .WithMetrics(builder =>
        builder
            .AddPrometheusExporter()
            .AddMeter(
                "System.Net.Http",
                "System.Runtime",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions.Diagnostics.HealthChecks",
                BskyMetrics.METRIC_METER_NAME
            )
            .AddAspNetCoreInstrumentation()
    );

builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

var bskyConfig = app.Configuration.GetSection("BskyConfig").Get<BskyConfigBlock>();
if (bskyConfig != null)
{
    app.MapGet(
        "/.well-known/did.json",
        () =>
            new Dictionary<string, dynamic>
            {
                { "@context", new string[] { "https://www.w3.org/ns/did/v1" } },
                { "id", bskyConfig.Identity.ServiceDid },
                {
                    "service",
                    new List<Dictionary<string, string>>
                    {
                        new()
                        {
                            { "id", "#bsky_fg" },
                            { "type", "BskyFeedGenerator" },
                            { "serviceEndpoint", $"https://{bskyConfig.Identity.Hostname}" },
                        },
                    }
                },
            }
    );
}

app.MapControllers();

app.MapHealthChecks("/healthz").DisableHttpMetrics();
app.MapPrometheusScrapingEndpoint();

app.Run();
