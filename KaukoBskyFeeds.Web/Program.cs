using FishyFlip;
using KaukoBskyFeeds.Db;
using KaukoBskyFeeds.Feeds.Registry;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;

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
    var dbPath = Path.Join(dataPath.DbDir, "jetstream.db");
    options.UseSqlite($"Data Source={dbPath}");
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBskyCache, BskyCache>();
builder.Services.AddSingleton(f =>
{
    var logger = f.GetService<ILogger<ATProtocol>>();
    return new ATProtocolBuilder().EnableAutoRenewSession(true).WithLogger(logger).Build();
});
builder.Services.AddSingleton<FeedRegistry>();

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

app.Run();
