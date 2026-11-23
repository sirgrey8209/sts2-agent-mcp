using System;
using System.IO;
using System.Linq;
using System.Text;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// The learning notes kept across runs, as a folder of small text files handled like Claude's memory tool: paths under
/// <c>/notes</c> and the commands view, create, str_replace, insert, delete and rename (same names and meanings), so the
/// agent edits a note in place instead of rewriting everything. Kept in the game's chat folder (notes/). The run brief
/// gives the agent the file list, <c>/notes/general.md</c> and the run's character file
/// (<c>/notes/characters/&lt;id&gt;.md</c>); the rest it reads when needed.
/// </summary>
internal static class NotesStore
{
    private const string Prefix = "/notes";
    private const int MaxFileChars = 20000;

    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "notes");

    private static string LegacyFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "notes.md");

    /// <summary>The old single notes file becomes /notes/general.md, once.</summary>
    private static void Migrate()
    {
        try
        {
            Directory.CreateDirectory(Root);
            string general = Path.Combine(Root, "general.md");
            if (File.Exists(LegacyFile) && !File.Exists(general))
            {
                File.Move(LegacyFile, general);
                Log.Info("chat: notes.md moved to notes/general.md");
            }
        }
        catch (Exception ex) { Log.Warn($"chat: notes migration failed: {ex.Message}"); }
    }

    /// <summary>A path under /notes mapped into the folder; rejects anything outside it.</summary>
    private static string Resolve(string? path)
    {
        string p = (path ?? "").Trim().Replace('\\', '/');
        if (p == "" || p == Prefix || p == Prefix + "/") return Root;
        if (!p.StartsWith(Prefix + "/", StringComparison.Ordinal)) p = Prefix + "/" + p.TrimStart('/');
        string relative = p[(Prefix.Length + 1)..];
        if (relative.Split('/').Any(part => part is "" or "." or "..")) throw new ToolError($"bad path '{path}'");
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.Ordinal)) throw new ToolError($"path outside {Prefix}: '{path}'");
        return full;
    }

    private static string Display(string full) =>
        full == Root ? Prefix : Prefix + "/" + Path.GetRelativePath(Root, full).Replace('\\', '/');

    public static string View(string? path, int[]? range)
    {
        Migrate();
        string full = Resolve(path);
        if (Directory.Exists(full))
        {
            var sb = new StringBuilder($"Directory {Display(full)}:\n");
            var entries = Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories).OrderBy(e => e, StringComparer.Ordinal).ToList();
            if (entries.Count == 0) sb.Append("(empty)");
            foreach (var entry in entries)
                sb.Append(Directory.Exists(entry) ? $"{Display(entry)}/\n" : $"{Display(entry)} ({new FileInfo(entry).Length} bytes)\n");
            return sb.ToString().TrimEnd();
        }
        if (!File.Exists(full)) throw new ToolError($"{Display(full)} does not exist");
        var lines = File.ReadAllLines(full);
        int from = 1, to = lines.Length;
        if (range is { Length: 2 })
        {
            from = Math.Max(1, range[0]);
            to = range[1] < 0 ? lines.Length : Math.Min(lines.Length, range[1]);
        }
        var text = new StringBuilder();
        for (int i = from; i <= to; i++) text.Append($"{i,6}\t{lines[i - 1]}\n");
        return text.Length == 0 ? "(empty file)" : text.ToString().TrimEnd('\n');
    }

    public static string Create(string? path, string? text)
    {
        Migrate();
        string full = Resolve(path);
        if (full == Root || Directory.Exists(full)) throw new ToolError("give a file path, e.g. /notes/general.md");
        Check(text ?? "");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text ?? "", new UTF8Encoding(false));
        return $"File created successfully at {Display(full)}";
    }

    public static string StrReplace(string? path, string? oldText, string? newText)
    {
        string full = ExistingFile(path);
        string text = File.ReadAllText(full);
        if (string.IsNullOrEmpty(oldText)) throw new ToolError("give old_str");
        int count = CountOf(text, oldText);
        if (count == 0) throw new ToolError($"old_str not found in {Display(full)}");
        if (count > 1) throw new ToolError($"old_str appears {count} times in {Display(full)}; make it unique");
        string updated = text.Replace(oldText, newText ?? "");
        Check(updated);
        File.WriteAllText(full, updated, new UTF8Encoding(false));
        return $"The note {Display(full)} has been edited.";
    }

    public static string Insert(string? path, int line, string? text)
    {
        string full = ExistingFile(path);
        var lines = File.ReadAllLines(full).ToList();
        if (line < 0 || line > lines.Count) throw new ToolError($"insert_line must be 0-{lines.Count}");
        lines.InsertRange(line, (text ?? "").Replace("\r", "").Split('\n'));
        string updated = string.Join("\n", lines) + "\n";
        Check(updated);
        File.WriteAllText(full, updated, new UTF8Encoding(false));
        return $"The note {Display(full)} has been edited.";
    }

    public static string Delete(string? path)
    {
        Migrate();
        string full = Resolve(path);
        if (full == Root) throw new ToolError($"cannot delete {Prefix} itself");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        else if (File.Exists(full)) File.Delete(full);
        else throw new ToolError($"{Display(full)} does not exist");
        return $"Deleted {Display(full)}";
    }

    public static string Rename(string? oldPath, string? newPath)
    {
        Migrate();
        string from = Resolve(oldPath), to = Resolve(newPath);
        if (from == Root || to == Root) throw new ToolError($"cannot rename {Prefix} itself");
        if (File.Exists(to) || Directory.Exists(to)) throw new ToolError($"{Display(to)} already exists");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(from)) Directory.Move(from, to);
        else if (File.Exists(from)) File.Move(from, to);
        else throw new ToolError($"{Display(from)} does not exist");
        return $"Renamed {Display(from)} to {Display(to)}";
    }

    /// <summary>
    /// For the run brief: the file list, the general notes and the notes for this run's character (each when there).
    /// Empty when there are no notes.
    /// </summary>
    public static string Brief(string? character)
    {
        Migrate();
        try
        {
            var files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append("파일: ").Append(string.Join(", ", files.Select(Display))).Append('\n');
            Append(sb, Path.Combine(Root, "general.md"));
            if (!string.IsNullOrWhiteSpace(character))
                Append(sb, Path.Combine(Root, "characters", character.Trim().ToLowerInvariant() + ".md"));
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: notes unreadable: {ex.Message}");
            return "";
        }
    }

    private static void Append(StringBuilder sb, string file)
    {
        if (!File.Exists(file)) return;
        string text = File.ReadAllText(file).Trim();
        if (text.Length > 0) sb.Append(Display(file)).Append(":\n").Append(text).Append('\n');
    }

    private static string ExistingFile(string? path)
    {
        Migrate();
        string full = Resolve(path);
        if (!File.Exists(full)) throw new ToolError($"{Display(full)} does not exist");
        return full;
    }

    private static void Check(string text)
    {
        if (text.Length > MaxFileChars) throw new ToolError($"a note may be at most {MaxFileChars} characters; split it into files");
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
