using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using KaukoBskyFeeds.Web;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Bleh: we may need to step up a directory to find the config file
var bskyConfigFilename = "bsky.config.json";
var bskyConfigPath = Path.Join(Directory.GetCurrentDirectory(), bskyConfigFilename);
if (!File.Exists(bskyConfigPath))
{
    // Move up
    var upOne = Path.Join(Directory.GetCurrentDirectory(), "..", bskyConfigFilename);
    if (File.Exists(upOne))
    {
        bskyConfigPath = upOne;
    }
    else
    {
        throw new Exception("Couldn't find bsky.config.json");
    }
}

builder.Configuration.AddJsonFile(bskyConfigPath);
builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        | System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBskyCache, BskyCache>();
builder.Services.AddSingleton<FeedProcessor>();

var app = builder.Build();

var bskyConfig = app.Configuration.GetSection("BskyConfig").Get<BskyConfigBlock>();
if (bskyConfig == null)
{
    throw new Exception("Failed to load config");
}

app.MapGet("/", () => "Hello World!");

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

var feedProcessor = app.Services.GetRequiredService<FeedProcessor>();
app.MapGet("/xrpc/app.bsky.feed.getFeedSkeleton", feedProcessor.GetFeedSkeleton);
app.MapGet("/xrpc/app.bsky.feed.describeFeedGenerator", feedProcessor.DescribeFeedGenerator);

if (app.Environment.IsDevelopment())
{
    app.MapGet("/previewFeed", feedProcessor.GetHydratedFeed);
}

if (bskyConfig.EnableInstall)
{
    app.MapPost("/install", feedProcessor.Install);
}

app.Run();
