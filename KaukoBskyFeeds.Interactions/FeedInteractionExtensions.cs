using Microsoft.Extensions.DependencyInjection;

namespace KaukoBskyFeeds.Interactions;

public static class FeedInteractionExtensions
{
    public static IServiceCollection AddFeedInteractionServices(
        this IServiceCollection collection,
        InteractionStorageProvider provider
    )
    {
        if (provider == InteractionStorageProvider.Redis)
        {
            collection.AddScoped<IInteractionCache, RedisInteractionCache>();
        }
        else if (provider == InteractionStorageProvider.Database)
        {
            collection.AddScoped<IInteractionCache, DbInteractionCache>();
        }
        else
        {
            collection.AddScoped<IInteractionCache, MemoryInteractionCache>();
        }

        return collection;
    }
}

public enum InteractionStorageProvider
{
    Memory,
    Database,
    Redis,
}
