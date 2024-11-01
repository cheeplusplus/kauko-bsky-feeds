using KaukoBskyFeeds;
using KaukoBskyFeeds.Bsky;
using KaukoBskyFeeds.FeedProcessor;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("bsky.config.json");
builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        | System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBskyCache, BskyCache>();

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
                    new Dictionary<string, string>
                    {
                        { "id", "#bsky_fg" },
                        { "type", "BskyFeedGenerator" },
                        { "serviceEndpoint", $"https://{bskyConfig.Identity.Hostname}" },
                    },
                }
            },
        }
);

var loggerFac = app.Services.GetRequiredService<ILoggerFactory>();
var bskyCache = app.Services.GetRequiredService<IBskyCache>();
var feedProcessor = new FeedProcessor(loggerFac, app.Configuration, bskyCache, bskyConfig); // TODO: Add as service
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
