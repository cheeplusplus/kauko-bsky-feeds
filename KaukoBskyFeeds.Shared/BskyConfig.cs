namespace KaukoBskyFeeds.Shared;

public record BskyConfigBlock(
    BskyConfigAuth Auth,
    BskyConfigIdentity Identity,
    Dictionary<string, BskyConfigFeedProcessor> FeedProcessors,
    BskyConfigWeb? Web,
    bool EnableInstall = false
);

public record BskyConfigAuth(string Username, string Password);

public record BskyConfigWeb(bool EnableDevEndpoints = false);

public record BskyConfigIdentity(string Hostname, string PublishedAtUri)
{
    public string ServiceDid => $"did:web:{Hostname}";
    public string BskyFeedGeneratorServiceEndpoint => $"https://{Hostname}";
}

public record BskyConfigFeedProcessor(string Type, BaseFeedConfig Config);

public record BaseFeedConfig(
    string DisplayName,
    string Description,
    bool RestrictToFeedOwner = false,
    bool Install = false
);

public static class BskyConfigExtensions
{
    public static DataDirInfo GetDataDir(string rootFile)
    {
        var configDir = Directory.GetCurrentDirectory();
        var bskyConfigPath = Path.Join(configDir, rootFile);

        var upCounter = 0;
        while (!File.Exists(bskyConfigPath) && upCounter < 4)
        {
            // Move up
            configDir = Path.Join(configDir, "..");
            bskyConfigPath = Path.Join(configDir, rootFile);

            upCounter++;
        }

        if (!File.Exists(bskyConfigPath))
        {
            throw new Exception("Couldn't find " + rootFile);
        }

        var dbDir = Path.Join(configDir, "data");
        return new DataDirInfo(bskyConfigPath, dbDir);
    }

    public record DataDirInfo(string ConfigPath, string DbDir);
}
