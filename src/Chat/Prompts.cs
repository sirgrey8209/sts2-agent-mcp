using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>How much the agent does in a run (docs/chat-roadmap.md).</summary>
internal enum PlayLevel { Commentary, CoLow, CoHigh, Auto }

/// <summary>
/// System prompts for the chat sessions, put together from fragments (docs/chat-roadmap.md, step 1), in this order:
/// <list type="number">
/// <item><c>system</c>: the situation, how to explain, and guidance per screen (combat, map, rewards, ...).</item>
/// <item><c>persona</c>: tone and character, when the file exists.</item>
/// <item>the play level (<c>level-*</c>).</item>
/// <item>tips for the run's character (<c>character-&lt;id&gt;</c>, e.g. character-ironclad), when it exists.</item>
/// <item><c>prefs</c>: the player's own wishes, when the file exists.</item>
/// </list>
/// The chat has one session per run (none at the main menu). The defaults are built into the mod (prompts/*.md); a file
/// with the same name in the game's chat folder (<see cref="UserDir"/>) replaces one. The learning notes are not in the
/// system prompt: they go into the run's first message (the run brief).
/// </summary>
internal static class Prompts
{
    public static string UserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "prompts");

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
    /// <param name="character">The run's character id (e.g. "IRONCLAD").</param>
    public static string System(PlayLevel level, string? character)
    {
        var parts = new List<string> { Fragment("system"), Optional("persona"), Fragment("level-" + LevelKey(level)) };
        if (!string.IsNullOrWhiteSpace(character)) parts.Add(Optional("character-" + character.Trim().ToLowerInvariant()));
        if (UserFile("prefs") is { } prefs) parts.Add("# 플레이어가 원하는 것\n\n" + prefs);
        return string.Join("\n\n", parts.Where(p => p.Length > 0));
    }

    private static string Fragment(string name) => UserFile(name) ?? Builtin(name) ?? "";

    /// <summary>A fragment that may not exist (persona, character tips): empty without a warning.</summary>
    private static string Optional(string name) => UserFile(name) ?? Builtin(name, quiet: true) ?? "";

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

    private static string? Builtin(string name, bool quiet = false)
    {
        using var stream = typeof(Prompts).Assembly.GetManifestResourceStream($"prompts.{name}.md");
        if (stream == null)
        {
            if (!quiet) Log.Warn($"chat: built-in prompt {name} missing");
            return null;
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }
}
