using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Knowledge-base documents (kb/ in the repo, installed next to each mod's DLL as <mod id>-kb/). They summarize game
/// behaviour that is not visible in game text (monster move effects, event outcomes), written from the decompiled
/// source of a specific game version.
/// </summary>
internal static class KbDocs
{
    public const string FolderName = ModInfo.Id + "-kb";

    private static readonly string? Root =
        Path.GetDirectoryName(typeof(KbDocs).Assembly.Location) is { } dir ? Path.Combine(dir, FolderName) : null;

    public static string? GameVersion { get; set; }

    // Tool markers such as the glossary marker are for the kb scripts, not the reader.
    private static readonly Regex HtmlComment = new(@"[ \t]*<!--.*?-->[ \t]*\r?\n?", RegexOptions.Singleline);

    /// <summary>
    /// Lines of the knowledge-base documents (monsters, events) that contain every word of <paramref name="query"/>
    /// (case-insensitive), as "FOLDER/ID: line", at most <paramref name="limit"/>.
    /// </summary>
    public static List<string> Grep(string query, int limit)
    {
        var result = new List<string>();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Root == null || words.Length == 0 || !Directory.Exists(Root)) return result;
        foreach (var file in Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string doc = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (!doc.Contains('/')) continue; // README and other notes at the top
            doc = doc[..^3];
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (!words.All(w => line.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                    result.Add($"{doc}: {line.Trim()}");
                    if (result.Count >= limit) return result;
                }
            }
            catch (Exception ex) { Log.Warn($"kb grep: {doc}: {ex.Message}"); }
        }
        return result;
    }

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
