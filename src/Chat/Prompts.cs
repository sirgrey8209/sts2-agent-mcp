using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>Who the chat talks to: the main-menu session, or the session of one run.</summary>
internal enum ChatRole { Control, Play }

/// <summary>How much the agent does in a run (docs/chat-roadmap.md).</summary>
internal enum PlayLevel { Commentary, CoLow, CoHigh, Auto }

/// <summary>
/// System prompts for the chat sessions, put together from fragments (docs/chat-roadmap.md, step 1): common tools and
/// answer style, the in-game panel, the role (control / play) and, for play, the play level. The defaults are built
/// into the mod (prompts/*.md); a file with the same name in the game's chat folder (<see cref="UserDir"/>) replaces
/// one. <c>prefs.md</c> there (the player's own wishes) is added when it exists. The learning notes and the run brief
/// are not in the system prompt: they go into the first message of a play session.
/// </summary>
internal static class Prompts
{
    public static string UserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "prompts");

    public static string NotesFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "notes.md");

    public static string LevelKey(PlayLevel level) => level switch
    {
        PlayLevel.Commentary => "commentary",
        PlayLevel.CoLow => "colow",
        PlayLevel.CoHigh => "cohigh",
        PlayLevel.Auto => "auto",
        _ => "colow",
    };

    public static string LevelName(PlayLevel level) => level switch
    {
        PlayLevel.Commentary => "코멘터리",
        PlayLevel.CoLow => "코플레이(Low)",
        PlayLevel.CoHigh => "코플레이(High)",
        PlayLevel.Auto => "오토플레이",
        _ => "",
    };

    public static PlayLevel? ParseLevel(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "commentary" or "코멘터리" => PlayLevel.Commentary,
        "colow" or "co-low" or "코플레이(low)" or "low" => PlayLevel.CoLow,
        "cohigh" or "co-high" or "코플레이(high)" or "high" => PlayLevel.CoHigh,
        "auto" or "오토" or "오토플레이" => PlayLevel.Auto,
        _ => null,
    };

    /// <summary>The system prompt for a session.</summary>
    public static string System(ChatRole role, PlayLevel level)
    {
        var parts = new List<string> { Fragment("common"), Fragment("ingame") };
        if (role == ChatRole.Control) parts.Add(Fragment("role-control"));
        else
        {
            parts.Add(Fragment("role-play"));
            parts.Add(Fragment("level-" + LevelKey(level)));
        }
        if (UserFile("prefs") is { } prefs) parts.Add("## 플레이어가 원하는 것\n\n" + prefs);
        return string.Join("\n\n", parts.Where(p => p.Length > 0));
    }

    /// <summary>The learning notes (empty when none).</summary>
    public static string Notes()
    {
        try { return File.Exists(NotesFile) ? File.ReadAllText(NotesFile).Trim() : ""; }
        catch (Exception ex) { Log.Warn($"chat: notes unreadable: {ex.Message}"); return ""; }
    }

    public static void SaveNotes(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NotesFile)!);
        File.WriteAllText(NotesFile, text.Trim() + "\n", new UTF8Encoding(false));
    }

    private static string Fragment(string name) => UserFile(name) ?? Builtin(name) ?? "";

    private static string? UserFile(string name)
    {
        try
        {
            string path = Path.Combine(UserDir, name + ".md");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: prompt {name} unreadable: {ex.Message}");
            return null;
        }
    }

    private static string? Builtin(string name)
    {
        using var stream = typeof(Prompts).Assembly.GetManifestResourceStream($"prompts.{name}.md");
        if (stream == null)
        {
            Log.Warn($"chat: built-in prompt {name} missing");
            return null;
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }
}
