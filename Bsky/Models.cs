using System.Text.Json.Serialization;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Bsky.Models;

public record CustomFeedRecord(
    string Did,
    string DisplayName,
    string Description,
    string CreatedAt,
    BlobRecord? Avatar = null
);

public record CreateCustomFeedRecord(
    string Collection,
    string Repo,
    CustomFeedRecord Record,
    string Rkey,
    bool Validate = true
);

// FishyFlip doesn't support what we're trying to do, and also doesn't expose the JsonTypeInfos
public record CustomRecordRef(string Uri, string Cid, string ValidationStatus);

[JsonSerializable(typeof(CustomFeedRecord))]
[JsonSerializable(typeof(CreateCustomFeedRecord))]
[JsonSerializable(typeof(CustomRecordRef))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
internal partial class SourceGenerationContext : JsonSerializerContext { }
