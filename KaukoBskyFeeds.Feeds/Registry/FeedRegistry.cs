using System.Reflection;
using FishyFlip;
using KaukoBskyFeeds.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KaukoBskyFeeds.Feeds.Registry;

public class FeedRegistry
{
    private readonly IConfigurationSection _bskyConfigSection;
    private readonly Dictionary<string, (Type, Type)> _feedTypes;
    private readonly List<IndividualFeed> _feeds;

    public FeedRegistry(IConfiguration configuration)
    {
        _bskyConfigSection = configuration.GetRequiredSection("BskyConfig");
        var bskyConfig =
            _bskyConfigSection.Get<BskyConfigBlock>()
            ?? throw new Exception("Missing configuration");

        // Collect all feed classes from the assembly
        _feedTypes = this.GetType()
            .Assembly.GetTypes()
            .Select(s => new { CType = s, CAttr = s.GetCustomAttribute<BskyFeedAttribute>() })
            .Where(w => w.CAttr != null)
            .ToDictionary(k => k.CAttr!.Name, v => (v.CType, v.CAttr!.ConfigType));

        // Combine configurations
        _feeds = bskyConfig
            .FeedProcessors.Select(feedCfg => new IndividualFeed(
                feedCfg.Value.Type,
                _feedTypes[feedCfg.Value.Type].Item1,
                _bskyConfigSection
                    .GetRequiredSection($"FeedProcessors:{feedCfg.Key}:Config")
                    .Get(_feedTypes[feedCfg.Value.Type].Item2)
                    ?? throw new Exception("Failed to parse feed configuration"),
                feedCfg.Value.Config,
                feedCfg.Key,
                $"{bskyConfig.Identity.PublishedAtUri}/{Constants.FeedType.Generator}/{feedCfg.Key}"
            ))
            .ToList();
    }

    public IEnumerable<IndividualFeed> AllFeeds => _feeds;
    public IEnumerable<string> AllFeedUris => _feeds.Select(s => s.FeedUri);

    public IFeed? GetFeedInstance(IServiceProvider sp, string feedUri)
    {
        var indvFeed = _feeds.SingleOrDefault(s => s.FeedUri == feedUri);
        if (indvFeed == null)
        {
            return null;
        }

        var inst = ActivatorUtilities.CreateInstance(sp, indvFeed.FeedClass, indvFeed.FeedConfig);
        return inst as IFeed;
    }

    public record IndividualFeed(
        string FeedType,
        Type FeedClass,
        object FeedConfig,
        BaseFeedConfig FeedBaseConfig,
        string FeedShortname,
        string FeedUri
    );
}
