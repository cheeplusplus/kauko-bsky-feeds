namespace KaukoBskyFeeds.Ingest.Jetstream.Models.Records;

public interface IAppBskyFeedWithSubject
{
    public AtStrongRef Subject { get; }
}
