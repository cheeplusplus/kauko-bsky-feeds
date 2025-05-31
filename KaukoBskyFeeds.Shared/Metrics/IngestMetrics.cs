using System.Diagnostics.Metrics;

namespace KaukoBskyFeeds.Shared.Metrics;

public class IngestMetrics
{
    public const string MetricMeterName = "KaukoBskyFeeds.Ingest";
    private readonly Counter<int> _ingestEventCounter;
    private readonly Gauge<double> _ingestBacklogGauge;
    private readonly Counter<int> _saveCountCounter;
    private readonly Histogram<double> _saveDurationHistogram;

    public IngestMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricMeterName);
        _ingestEventCounter = meter.CreateCounter<int>(
            $"{MetricMeterName}.records",
            description: "Ingest records"
        );
        _ingestBacklogGauge = meter.CreateGauge<double>(
            $"{MetricMeterName}.backlog",
            description: "Ingest backlog",
            unit: "seconds"
        );
        _saveCountCounter = meter.CreateCounter<int>(
            $"{MetricMeterName}.save.count",
            description: "Records saved"
        );
        _saveDurationHistogram = meter.CreateHistogram<double>(
            $"{MetricMeterName}.save.duration",
            description: "Save duration",
            unit: "seconds"
        );
    }

    public void IngestEvent(string collection, DateTime eventTime)
    {
        var tags = new KeyValuePair<string, object?>(Tags.AtprotoCollection, collection);

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
        void Add(string name, int size) =>
            _saveCountCounter.Add(size, new KeyValuePair<string, object?>(Tags.DbTableName, name));
        Add("Post", posts);
        Add("Like", likes);
        Add("QuotePost", quotePosts);
        Add("PostReply", postReplies);
        Add("PostRepost", postReposts);
        _saveDurationHistogram.Record(saveDuration.TotalSeconds);
    }
}
