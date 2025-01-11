namespace KaukoBskyFeeds.Ingest;

public record IngestConfig(
    int SaveMaxSec = 10,
    int SaveMaxSize = 10000,
    bool Verbose = false,
    bool ConsumeHistoricFeed = false,
    string? SingleCollection = null
);
