using FishyFlip;
using KaukoBskyFeeds.Ingest.Jetstream;
using KaukoBskyFeeds.Lofi;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("bsky.config.json");

builder.Services.AddSingleton(f =>
{
    var logger = f.GetService<ILogger<ATProtocol>>();
    return new ATProtocolBuilder().EnableAutoRenewSession(true).WithLogger(logger).Build();
});

// builder.Services.AddScoped<IJetstreamConsumer, JetstreamConsumerNativeWs>();
builder.Services.AddScoped<IJetstreamConsumer, JetstreamConsumerWSC>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<BskyCache>();
builder.Services.AddHostedService<LofiWorker>();

IHost host = builder.Build();

host.Run();
