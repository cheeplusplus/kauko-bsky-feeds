using FishyFlip.Models;

namespace KaukoBskyFeeds.Lofi;

public static class LofiUtils
{
    public static string Indent(string text, int tabWidth = 4)
    {
        var splitText = text.Split("\n");
        var padding = "".PadLeft(tabWidth);
        var mapped = splitText.Select(s => padding + s.TrimEnd());
        return string.Join('\n', mapped);
    }

    public static string TerminalURL(string caption, string url) =>
        $"\u001B]8;;{url}\a{caption}\u001B]8;;\a";

    public static string AtUriToBskyUrl(ATUri uri, string? handle = null)
    {
        var profileName = handle ?? uri.Did?.ToString() ?? "ERR";
        return $"https://bsky.app/profile/{profileName}/post/{uri.Rkey}";
    }
}
