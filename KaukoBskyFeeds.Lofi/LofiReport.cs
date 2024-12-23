using System.Text.Json;
using System.Text.Json.Serialization;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Lofi;

public record LofiReport(PostView Post, PostView? ReplyParentPost, PostView? ReplyRootPost)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull,
        };

    public void Print(LofiConfig? opts = null)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("".PadRight(Console.WindowWidth / 2, '-'));

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        var postTime = Post.Record?.CreatedAt?.ToLocalTime() ?? Post.IndexedAt.ToLocalTime();
        var dateStr = (DateTime.Now - postTime).Days > 0 ? postTime.ToShortDateString() + " " : "";
        Console.Write($"[{dateStr}{postTime.ToShortTimeString()}] ");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"{Post.Author.DisplayName} ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"(@{Post.Author.Handle}) ");

        Console.ForegroundColor = ConsoleColor.Gray;
        if (ReplyParentPost != null)
        {
            Console.Write($"replied to ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"@{ReplyParentPost.Author.Handle}");
        }
        else
        {
            Console.Write("posted");
        }
        Console.Write(" ");

        Console.ForegroundColor = ConsoleColor.Blue;
        var postUrl = LofiUtils.AtUriToBskyUrl(Post.Uri, Post.Author.Handle);
        Console.Write(LofiUtils.TerminalURL($"({postUrl})", postUrl));
        Console.ForegroundColor = ConsoleColor.Gray;

        Console.WriteLine();

        if (ReplyParentPost != null)
        {
            static void printReplyPost(PostView pv, string inner)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"  [{pv.Record?.CreatedAt?.ToLocalTime().ToString("g")}] ");
                Console.Write(pv.Author.DisplayName);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" @{pv.Author.Handle}");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($" {inner}:");
                Console.ForegroundColor = ConsoleColor.Cyan;

                if (pv.Record?.Text != null)
                {
                    var textLines = pv.Record.Text.Split('\n');
                    foreach (var iline in textLines)
                    {
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write("  | ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(iline.TrimEnd());
                    }
                }
                else
                {
                    Console.WriteLine("    (no text)");
                }

                Console.ForegroundColor = ConsoleColor.Black;
                Console.WriteLine("  ...  ");
            }

            if (ReplyRootPost != null)
            {
                printReplyPost(ReplyRootPost, "posted");
            }
            if (
                ReplyParentPost != null
                && ReplyParentPost.Uri.ToString() != ReplyRootPost?.Uri.ToString()
            )
            {
                printReplyPost(ReplyParentPost, "replied to above");
            }
        }

        if (string.IsNullOrWhiteSpace(Post.Record?.Text))
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("(no text)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(Post.Record.Text);
        }

        // Line 3
        if (Post.Embed != null && (opts?.PrintEmbeds ?? false))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            if (Post.Embed is ImageViewEmbed imagesEmbed)
            {
                Console.Write($"Has {imagesEmbed.Images.Length} image(s): ");
                var imgLabels = imagesEmbed.Images.Select(img =>
                    LofiUtils.TerminalURL(
                        string.IsNullOrWhiteSpace(img.Alt) ? "(image)" : img.Alt,
                        img.Fullsize
                    )
                );
                Console.Write(string.Join(" > ", imgLabels));
            }
            else if (Post.Embed is RecordViewEmbed recordEmbed)
            {
                var embedUrl = LofiUtils.AtUriToBskyUrl(
                    recordEmbed.Post.Uri,
                    recordEmbed.Post.Author.Handle
                );
                Console.Write("References post: ");
                Console.Write(LofiUtils.TerminalURL(embedUrl, embedUrl));
            }
            else if (Post.Embed is ExternalViewEmbed externEmbed)
            {
                Console.Write("Has external link: ");
                Console.Write(externEmbed.External.Uri ?? externEmbed.External.ToString());
            }
            else
            {
                Console.Write("Other embed: ");
                Console.Write(JsonSerializer.Serialize(Post.Embed, JsonOptions));
            }
            Console.WriteLine();
        }
    }
};
