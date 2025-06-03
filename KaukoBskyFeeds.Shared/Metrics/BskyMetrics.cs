using System.Diagnostics;
using System.Diagnostics.Metrics;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Shared.Metrics;

public class BskyMetrics
{
    public const string MetricMeterName = "KaukoBskyFeeds.Shared";
    private readonly Histogram<double> _bskyApiHistogram;

    public BskyMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricMeterName);
        _bskyApiHistogram = meter.CreateHistogram<double>(
            $"{MetricMeterName}.bsky.api",
            description: "API request latency",
            unit: "seconds"
        );
    }

    public void RecordBskyRequest(string xrpcName, int responseStatus, double duration)
    {
        _bskyApiHistogram.Record(
            duration,
            new KeyValuePair<string, object?>(Tags.AtprotoXrpcPath, xrpcName),
            new KeyValuePair<string, object?>(Tags.AtprotoXrpcStatus, responseStatus)
        );
    }
}

public static class BskyMetricsExtensions
{
    /// <summary>
    /// Record a request to the metrics provider.
    /// </summary>
    /// <param name="apiRequest">API request task being made.</param>
    /// <param name="metrics">Metrics provider.</param>
    /// <param name="xrpcName">XRPC name.</param>
    /// <typeparam name="T">Result type.</typeparam>
    /// <returns>Completed API response.</returns>
    public static async Task<Result<T>> Record<T>(
        this Task<Result<T>> apiRequest,
        BskyMetrics metrics,
        string xrpcName
    )
    {
        if (apiRequest.IsCompleted)
        {
            // No use recording time if the Task is already completed
            return await apiRequest;
        }

        var sw = Stopwatch.StartNew();
        var r = await apiRequest;
        sw.Stop();
        metrics.RecordBskyRequest(
            xrpcName,
            r.Match(_ => 200, f => f.StatusCode),
            sw.Elapsed.TotalSeconds
        );
        return r;
    }
}
