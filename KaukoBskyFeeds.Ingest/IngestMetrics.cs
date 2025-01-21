using System.Diagnostics.Metrics;

namespace KaukoBskyFeeds.Ingest;

public class IngestMetrics
{
    public const string METRIC_METER_NAME = "KaukoBskyFeeds.Ingest";
    private readonly Counter<int> _ingestEventCounter;
    private readonly Gauge<double> _ingestBacklog;

    public IngestMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(METRIC_METER_NAME);
        _ingestEventCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.records",
            description: "Ingest records"
        );
        _ingestBacklog = meter.CreateGauge<double>(
            $"{METRIC_METER_NAME}.backlog",
            description: "Ingest backlog",
            unit: "seconds"
        );
    }

    public void IngestEvent(string collection, DateTime eventTime)
    {
        var tags = new KeyValuePair<string, object?>("atproto.collection", collection);

        _ingestEventCounter.Add(1, tags);

        var timeDiff = DateTime.UtcNow - eventTime;
        _ingestBacklog.Record(timeDiff.TotalSeconds, tags);
    }
}
