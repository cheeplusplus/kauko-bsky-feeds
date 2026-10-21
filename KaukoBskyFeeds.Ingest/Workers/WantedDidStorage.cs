using System.Collections.Immutable;
using System.ComponentModel;

namespace KaukoBskyFeeds.Ingest.Workers;

[ImmutableObject(true)]
public sealed class WantedDidStorage
{
    public ImmutableArray<string> Values { get; }

    public WantedDidStorage(ImmutableArray<string> values) => Values = values;

    public bool Contains(string did) => Values.Contains(did);
}
