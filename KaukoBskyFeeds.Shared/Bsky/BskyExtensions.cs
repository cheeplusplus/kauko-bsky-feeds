using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using FishyFlip;
using FishyFlip.Models;
using FishyFlip.Tools.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace KaukoBskyFeeds.Shared.Bsky;

public delegate Task<(IEnumerable<T>?, string?)> BskyPageableList<T>(
    string? cursor,
    CancellationToken cancellationToken = default
);

public static class BskyExtensions
{
    // copied from https://github.com/drasticcactions/FishyFlip/blob/825143dc1b4370f9749ab23f3074e207adc63eea/src/FishyFlip/Lexicon/SourceGenerationContext.g.cs#L13
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
            collection.AddSingleton<IConnectionMultiplexer>(builder =>
                ConnectionMultiplexer.Connect(redisConnectionString)
            );
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
        collection.AddSingleton(BskyCacheAutoRetry.Create);
        collection.AddSingleton(f =>
        {
            var logger = f.GetService<ILogger<ATProtocol>>();
            return new ATProtocolBuilder().EnableAutoRenewSession(true).WithLogger(logger).Build();
        });
        collection.AddSingleton<IBskyApi, BskyApi>();

        return collection;
    }

    /// <summary>
    /// Wrap a BskyPageableList callback into an IAsyncEnumerable
    /// </summary>
    /// <param name="callback">Callback function</param>
    /// <param name="delayMs">How long to delay between pages</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <typeparam name="T">Data type</typeparam>
    /// <returns>IAsyncEnumerable of results</returns>
    public static async IAsyncEnumerable<T> GetAllResultsEnumerable<T>(
        BskyPageableList<T> callback,
        int? delayMs,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        string? cursor = null;
        if (cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        while (true)
        {
            var (paged, curCursor) = await callback(cursor, cancellationToken);
            if (paged != null)
            {
                foreach (var page in paged)
                {
                    yield return page;
                }
            }

            if (curCursor == null || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (delayMs != null && delayMs > 0)
            {
                await Task.Delay(delayMs.Value, cancellationToken);
            }

            cursor = curCursor;
        }
    }

    /// <summary>
    /// Wrap GetAllResultsEnumerable with automatic cursor support
    /// </summary>
    /// <param name="action">Action to perform</param>
    /// <param name="valueSelector">Value selector</param>
    /// <param name="cursorSelector">Cursor selector</param>
    /// <param name="delayMs">How long to delay between pages</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <typeparam name="TOut">Output type</typeparam>
    /// <typeparam name="TIn">InputType</typeparam>
    /// <returns>IAsyncEnumerable of results</returns>
    public static IAsyncEnumerable<TOut> GetAllResultsCursorEnumerable<TOut, TIn>(
        Func<string?, CancellationToken, Task<TIn>> action,
        Func<TIn, IEnumerable<TOut>?> valueSelector,
        Func<TIn, string?> cursorSelector,
        int? delayMs = null,
        CancellationToken cancellationToken = default
    )
    {
        BskyPageableList<TOut> getPage = async (cursor, ict) =>
        {
            var d = await action(cursor, ict);
            return (valueSelector(d), cursorSelector(d));
        };

        return GetAllResultsEnumerable(getPage, delayMs, cancellationToken);
    }

    /// <summary>
    /// Collect an IAsyncEnumerable to a List.
    /// Make sure to set a limit if you're using an unbounded datasource source.
    /// </summary>
    /// <param name="list">Enumerable to collect</param>
    /// <param name="selector">Value selector</param>
    /// <param name="filter">Filter selector</param>
    /// <param name="limit">Output item limit (after filter)</param>
    /// <typeparam name="TIn">Input type</typeparam>
    /// <typeparam name="TOut">Output type</typeparam>
    /// <returns>Collected list</returns>
    public static async Task<IEnumerable<TOut>> ToListAsync<TIn, TOut>(
        this IAsyncEnumerable<TIn> list,
        Func<TIn, TOut> selector,
        Func<TIn, bool>? filter = null,
        int? limit = null
    )
    {
        var results = new List<TOut>();
        await foreach (var val in list)
        {
            if (filter == null || filter(val))
            {
                results.Add(selector(val));
            }

            if (results.Count > limit)
            {
                break;
            }
        }

        return results;
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> list)
        where T : class
    {
        return list.Where(w => w != null).Cast<T>();
    }

    public static DateTime AsUtc(this DateTime dt)
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
        return DateTimeOffset.FromUnixTimeMilliseconds(us / 1000).DateTime.AsUtc();
    }

    public static string AsSafeKey(this ATUri uri)
    {
        return HttpUtility.UrlEncode(uri.ToString());
    }

    public static string AsSafeKey(this ATDid did)
    {
        return HttpUtility.UrlEncode(did.ToString());
    }
}

public class NotLoggedInException : Exception
{
    public NotLoggedInException()
        : base("Not logged in!") { }
}

public class FeedProhibitedException : Exception { }
