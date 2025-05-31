using System.Reflection;
using FishyFlip.Models;
using KaukoBskyFeeds.Shared;
using KaukoBskyFeeds.Shared.Bsky;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KaukoBskyFeeds.Feeds.Registry;

public class FeedRegistry
{
    private readonly List<RegisteredFeed> _feeds;

    public FeedRegistry(IConfiguration configuration)
    {
        var bskyConfigSection = configuration.GetRequiredSection("BskyConfig");
        var bskyConfig =
            bskyConfigSection.Get<BskyConfigBlock>()
            ?? throw new Exception("Missing configuration");

        // Collect all feed classes from the assembly
        Dictionary<string, (Type, Type)> feedTypes = this.GetType()
            .Assembly.GetTypes()
            .Select(s => new { CType = s, CAttr = s.GetCustomAttribute<BskyFeedAttribute>() })
            .Where(w => w.CAttr != null)
            .ToDictionary(k => k.CAttr!.Name, v => (v.CType, v.CAttr!.ConfigType));

        // Combine configurations
        _feeds = bskyConfig
            .FeedProcessors.Select(feedCfg => new RegisteredFeed(
                feedCfg.Value.Type,
                feedTypes[feedCfg.Value.Type].Item1,
                bskyConfigSection
                    .GetRequiredSection($"FeedProcessors:{feedCfg.Key}:Config")
                    .Get(feedTypes[feedCfg.Value.Type].Item2)
                    ?? throw new Exception("Failed to parse feed configuration of " + feedCfg.Key),
                feedCfg.Value.Config,
                feedCfg.Key,
                $"{bskyConfig.Identity.PublishedAtUri}/{BskyConstants.CollectionTypeFeedGenerator}/{feedCfg.Key}"
            ))
            .ToList();
    }

    public IEnumerable<RegisteredFeed> AllFeeds => _feeds;
    public IEnumerable<string> AllFeedUris => _feeds.Select(s => s.FeedUri);

    public IFeed? GetFeedInstance(IServiceProvider sp, string feedUri)
    {
        var indvFeed = _feeds.SingleOrDefault(s => s.FeedUri == feedUri);
        if (indvFeed == null)
        {
            return null;
        }

        var feedMeta = new FeedInstanceMetadata(ATUri.Create(feedUri));

        var inst = ActivatorUtilities.CreateInstance(
            sp,
            indvFeed.FeedClass,
            indvFeed.FeedConfig,
            feedMeta
        );

        if (inst is not IFeed feedInst)
        {
            throw new Exception("Failed to activate feed instance for " + feedUri);
        }
        return feedInst;
    }

    public record RegisteredFeed(
        string FeedType,
        Type FeedClass,
        object FeedConfig,
        BaseFeedConfig FeedBaseConfig,
        string FeedShortname,
        string FeedUri
    );
}
