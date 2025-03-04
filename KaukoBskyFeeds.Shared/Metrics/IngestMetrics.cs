using System.Diagnostics.Metrics;

namespace KaukoBskyFeeds.Shared.Metrics;

public class IngestMetrics
{
    public const string METRIC_METER_NAME = "KaukoBskyFeeds.Ingest";
    private readonly Counter<int> _ingestEventCounter;
    private readonly Gauge<double> _ingestBacklogGauge;
    private readonly Counter<int> _saveCountCounter;
    private readonly Histogram<double> _saveDurationHistogram;

    public IngestMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(METRIC_METER_NAME);
        _ingestEventCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.records",
            description: "Ingest records"
        );
        _ingestBacklogGauge = meter.CreateGauge<double>(
            $"{METRIC_METER_NAME}.backlog",
            description: "Ingest backlog",
            unit: "seconds"
        );
        _saveCountCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.save.count",
            description: "Records saved"
        );
        _saveDurationHistogram = meter.CreateHistogram<double>(
            $"{METRIC_METER_NAME}.save.duration",
            description: "Save duration",
            unit: "seconds"
        );
    }

    public void IngestEvent(string collection, DateTime eventTime)
    {
        var tags = new KeyValuePair<string, object?>(Tags.ATPROTO_COLLECTION, collection);

        _ingestEventCounter.Add(1, tags);

        var timeDiff = DateTime.UtcNow - eventTime;
        _ingestBacklogGauge.Record(timeDiff.TotalSeconds, tags);
    }

    public void TrackSave(
        TimeSpan saveDuration,
        int posts,
        int likes,
        int quotePosts,
        int postReplies,
        int postReposts
    )
    {
        void add(string name, int size) =>
            _saveCountCounter.Add(
                size,
                new KeyValuePair<string, object?>(Tags.DB_TABLE_NAME, name)
            );
        add("Post", posts);
        add("Like", likes);
        add("QuotePost", quotePosts);
        add("PostReply", postReplies);
        add("PostRepost", postReposts);
        _saveDurationHistogram.Record(saveDuration.TotalSeconds);
    }
}
