using KaukoBskyFeeds.Redis.FastStore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KaukoBskyFeeds.Redis;

public static class Builder
{
    public static IServiceCollection AddBskyRedis(
        this IServiceCollection services,
        string connectionString
    )
    {
        // Redis
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
        });
        services.AddSingleton<IConnectionMultiplexer>(impl =>
        {
            return ConnectionMultiplexer.Connect(connectionString);
        });
        services.AddSingleton(impl =>
        {
            var multiplexer = impl.GetRequiredService<IConnectionMultiplexer>();
            return multiplexer.GetDatabase();
        });

        // Our stuff
        services.AddSingleton<IBskyCache, BskyCache>();
        services.AddSingleton<IFastStore, RedisFastStore>();

        return services;
    }
}
