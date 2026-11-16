using System;
using System.IO;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Knowledge-base documents (kb/ in the repo, installed next to the DLL as STS2AgentMcp-kb/). They summarize game
/// behaviour that is not visible in game text (monster move effects, event outcomes), written from the decompiled
/// source of a specific game version.
/// </summary>
internal static class KbDocs
{
    public const string FolderName = "STS2AgentMcp-kb";

    private static readonly string? Root =
        Path.GetDirectoryName(typeof(KbDocs).Assembly.Location) is { } dir ? Path.Combine(dir, FolderName) : null;

    public static string? GameVersion { get; set; }

    // Tool markers such as the glossary marker are for the kb scripts, not the reader.
    private static readonly Regex HtmlComment = new(@"[ \t]*<!--.*?-->[ \t]*\r?\n?", RegexOptions.Singleline);

    /// <summary>The document body for a model, or null. Notes when it was written for another game version.</summary>
    public static string? For(AbstractModel model)
    {
        string? folder = model switch
        {
            MonsterModel => "monsters",
            EventModel => "events",
            _ => null,
        };
        if (folder == null || Root == null) return null;
        string path = Path.Combine(Root, folder, model.Id.Entry + ".md");
        if (!File.Exists(path)) return null;

        try
        {
            string text = File.ReadAllText(path);
            string? docVersion = null;
            if (text.StartsWith("---", StringComparison.Ordinal))
            {
                int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (end > 0)
                {
                    foreach (var line in text[3..end].Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("game_version:", StringComparison.Ordinal))
                            docVersion = trimmed["game_version:".Length..].Trim();
                    }
                    text = text[(end + 4)..];
                }
            }
            text = HtmlComment.Replace(text, "");
            text = text.Trim();
            if (docVersion != null && GameVersion != null && docVersion != GameVersion)
                text = $"(kb written for {docVersion}; running {GameVersion} — may be outdated)\n" + text;
            return text;
        }
        catch (Exception ex)
        {
            return $"(kb document unreadable: {ex.Message})";
        }
    }
}
