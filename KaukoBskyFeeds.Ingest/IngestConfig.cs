namespace KaukoBskyFeeds.Ingest;

public record IngestConfig(
    int SaveMaxSec = 10,
    int SaveMaxSize = 10000,
    bool Verbose = false,
    bool ConsumeHistoricFeed = false,
    string? SingleCollection = null,
    Dictionary<string, IngestFilterConfig>? Filter = null
);

public record IngestFilterConfig(
    List<string>? UserFollowsAndLists = null,
    List<string>? Dids = null,
    List<string>? ListUris = null
);
