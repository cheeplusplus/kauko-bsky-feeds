using System.Collections.Immutable;
using System.ComponentModel;

namespace KaukoBskyFeeds.Ingest.Workers;

/// <summary>
/// An immutable storage of wanted DIDs for use with HybridCache
/// </summary>
[ImmutableObject(true)]
public sealed class WantedDidStorage(ImmutableArray<string> values)
{
    public ImmutableArray<string> Values { get; } = values;
}

/// <summary>
/// Soft wrapper of <see cref="WantedDidStorage"/> for faster lookup
/// </summary>
public sealed class WantedDidStorageProvider(WantedDidStorage? storage)
{
    private readonly HashSet<string> _hashSet = [.. storage?.Values ?? []];

    public bool Contains(string did) => _hashSet.Contains(did);
}
