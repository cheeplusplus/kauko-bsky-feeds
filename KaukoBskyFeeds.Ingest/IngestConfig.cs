namespace KaukoBskyFeeds.Ingest;

public record IngestConfig(
    bool Verbose = false,
    bool ConsumeHistoricFeed = false,
    string? SingleCollection = null
);
