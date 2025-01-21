using System.Diagnostics.Metrics;

namespace KaukoBskyFeeds.Ingest.Jetstream;

public class JetstreamMetrics
{
    public const string METRIC_METER_NAME = "KaukoBskyFeeds.Ingest.Jetstream";
    private readonly Counter<int> _reconnectCounter;
    private readonly Counter<int> _connectionErrorCounter;
    private readonly Counter<int> _eventRawCounter;
    private readonly Counter<int> _eventRawErrorCounter;
    private readonly Histogram<long> _eventRawSizeCompressedHistogram;
    private readonly Histogram<long> _eventRawSizeUncompressedHistogram;
    private readonly Counter<int> _eventParsedCounter;
    private readonly Counter<int> _eventParseErrorCounter;

    public JetstreamMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(METRIC_METER_NAME);
        _reconnectCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.connection.reconnect",
            description: "Websocket reconnections"
        );
        _connectionErrorCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.connection.error",
            description: "Websocket errors"
        );
        _eventRawCounter = meter.CreateCounter<int>("event.raw", description: "Raw events");
        _eventRawErrorCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.event.raw.error",
            description: "Raw event errors"
        );
        _eventRawSizeCompressedHistogram = meter.CreateHistogram<long>(
            $"{METRIC_METER_NAME}.event.size.compressed",
            description: "Compressed event size",
            unit: "bytes"
        );
        _eventRawSizeUncompressedHistogram = meter.CreateHistogram<long>(
            $"{METRIC_METER_NAME}.event.size.uncompressed",
            description: "Uncompressed event size",
            unit: "bytes"
        );
        _eventParsedCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.event.parsed",
            description: "Parsed events"
        );
        _eventParseErrorCounter = meter.CreateCounter<int>(
            $"{METRIC_METER_NAME}.event.parsed.error",
            description: "Event parse errors"
        );
    }

    public void WsReconnect(string host)
    {
        _reconnectCounter.Add(1, new KeyValuePair<string, object?>("ws.host", host));
    }

    public void WsError(string host, string errorClass)
    {
        _connectionErrorCounter.Add(
            1,
            new KeyValuePair<string, object?>("ws.host", host),
            new KeyValuePair<string, object?>("error.class", errorClass)
        );
    }

    public void SawEvent(long uncompressedSize, long? compressedSize = null)
    {
        _eventRawCounter.Add(1);
        _eventRawSizeUncompressedHistogram.Record(uncompressedSize);
        if (compressedSize != null && compressedSize.HasValue)
        {
            _eventRawSizeCompressedHistogram.Record(compressedSize.Value);
        }
    }

    public void SawEventGenericError(string errorClass)
    {
        _eventRawErrorCounter.Add(1, new KeyValuePair<string, object?>("error.class", errorClass));
    }

    public void SawEventParsed(string collection)
    {
        _eventParsedCounter.Add(
            1,
            new KeyValuePair<string, object?>("atproto.collection", collection)
        );
    }

    public void SawEventParseError(string errorClass)
    {
        _eventParseErrorCounter.Add(
            1,
            new KeyValuePair<string, object?>("error.class", errorClass)
        );
    }
}
