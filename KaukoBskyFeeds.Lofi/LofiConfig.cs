namespace KaukoBskyFeeds.Lofi;

public record LofiConfig(
    bool CruiseOwnFeed = true,
    bool HideReplies = false,
    bool HideRepliesToNotFollowing = false,
    bool PrintEmbeds = true,
    int? BackfillMinutes = 0
);
