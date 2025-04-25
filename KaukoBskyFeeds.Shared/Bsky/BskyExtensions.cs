using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KaukoBskyFeeds.Shared.Bsky;

public delegate Task<(IEnumerable<T>?, string?)> BskyPageableList<T>(
    string? cursor,
    CancellationToken cancellationToken = default
);

public static class BskyExtensions
{
    // copied from https://github.com/drasticactions/FishyFlip/blob/825143dc1b4370f9749ab23f3074e207adc63eea/src/FishyFlip/Lexicon/SourceGenerationContext.g.cs#L13
    // _proto.Proto.Options.JsonSerializerOptions would work if we weren't using any custom types
    public static readonly JsonSerializerOptions BskyJso = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new ATUriJsonConverter(),
            new ATCidJsonConverter(),
            new ATHandleJsonConverter(),
            new ATDidJsonConverter(),
            new ATIdentifierJsonConverter(),
            new ATWebSocketCommitTypeConverter(),
            new ATWebSocketEventConverter(),
            new ATObjectJsonConverter(),
        },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition =
            JsonIgnoreCondition.WhenWritingNull | JsonIgnoreCondition.WhenWritingDefault,
    };

    public static IServiceCollection AddBskyServices(
        this IServiceCollection collection,
        string? redisConnectionString = null
    )
    {
        if (redisConnectionString != null)
        {
            collection.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
            });
        }

        collection.AddKeyedSingleton<JsonSerializerOptions>(
            typeof(IHybridCacheSerializer<>),
            BskyJso
        );
        collection.AddHybridCache();
        collection.AddSingleton<IBskyCache, BskyCache>();
        collection.AddSingleton(f =>
        {
            var logger = f.GetService<ILogger<ATProtocol>>();
            return new ATProtocolBuilder().EnableAutoRenewSession(true).WithLogger(logger).Build();
        });

        return collection;
    }

    public static async Task<IEnumerable<T>> GetAllResults<T>(
        BskyPageableList<T> callback,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        string? cursor = null;
        List<T> results = [];

        if (cancellationToken.IsCancellationRequested)
        {
            return results;
        }

        while (true)
        {
            var (paged, curCursor) = await callback(cursor, cancellationToken);
            if (paged != null)
            {
                results.AddRange(paged);
            }

            if (curCursor == null || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            cursor = curCursor;
        }

        return results;
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> list)
        where T : class
    {
        return list.Where(w => w != null).Cast<T>();
    }

    public static DateTime AsUTC(this DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return dt;
        }
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    public static long ToMicroseconds(this DateTime dt)
    {
        return ((DateTimeOffset)dt).ToUnixTimeMilliseconds() * 1000;
    }

    public static DateTime FromMicroseconds(long us)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(us / 1000).DateTime.AsUTC();
    }
}

public class ATDidComparer : IEqualityComparer<ATDid>
{
    public bool Equals(ATDid? x, ATDid? y)
    {
        return x?.Handler == y?.Handler;
    }

    public int GetHashCode([DisallowNull] ATDid obj)
    {
        return obj.GetHashCode();
    }
}

public class NotLoggedInException : Exception
{
    public NotLoggedInException()
        : base("Not logged in!") { }
}

public class FeedProhibitedException : Exception { }
