namespace KaukoBskyFeeds.Db;

interface IPostRecord
{
    PostRecordRef Ref { get; }
    DateTime EventTime { get; }
    long EventTimeUs { get; }
}

interface IPostInteraction : IPostRecord
{
    string ParentDid { get; }
    string ParentRkey { get; }
    PostRecordRef ParentRef { get; }
}

public record PostRecordRef(string Did, string Rkey);
