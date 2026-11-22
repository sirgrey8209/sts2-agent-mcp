using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2AgentMcp.Host;

/// <summary>
/// STS2AgentChat.conf next to the DLL. Created on first run with a random bearer token; the chat settings of an older
/// STS2AgentMcp.conf in the same folder (when the chat was part of that mod) are carried over then.
/// </summary>
internal sealed class Config
{
    public const string FileName = "STS2AgentChat.conf";
    private const string LegacyFileName = "STS2AgentMcp.conf";
    private static readonly string[] LegacyKeys =
        { "capture_dir", "chat_width", "chat_agent", "chat_auto_connect", "chat_play_level", "claude_path" };

    /// <summary>The chat's own MCP endpoint (its Claude Code sessions and development tools).</summary>
    public int Port { get; private set; } = 15528;
    public string Token { get; private set; } = "";
    /// <summary>"instant" (default) forces FastMode=Instant at startup; "keep" leaves the player's setting.</summary>
    public string FastMode { get; private set; } = "instant";
    /// <summary>Where screenshots and card sheets are written. Null = %TEMP%\sts2-agent-mcp.</summary>
    public string? CaptureDir { get; private set; }
    /// <summary>Chat panel width in units of a 1080-tall window (the panel scales with window height, not width).</summary>
    public float ChatWidth { get; private set; } = 480;
    /// <summary>Claude Code executable. Null = search PATH and the usual install folders.</summary>
    public string? ClaudePath { get; private set; }
    /// <summary>The chat folded away (only the Neow badge at the left edge); the game has the whole window.</summary>
    public bool ChatCollapsed { get; private set; }
    /// <summary>Connect without asking once the login works (the card's "connect automatically" box).</summary>
    public bool ChatAutoConnect { get; private set; }
    /// <summary>The agent card opened last ("claude" / "codex"), opened again at start.</summary>
    public string? ChatAgent { get; private set; }
    /// <summary>How much the agent does in a run (commentary / colow / cohigh / auto; docs/chat-roadmap.md).</summary>
    public string ChatPlayLevel { get; private set; } = "colow";

    public string? FilePath { get; private set; }

    public static Config Load()
    {
        var config = new Config();
        string? dir = Path.GetDirectoryName(typeof(Config).Assembly.Location);
        if (dir == null)
        {
            config.Token = NewToken();
            return config;
        }

        config.FilePath = Path.Combine(dir, FileName);
        bool exists = File.Exists(config.FilePath);
        JsonObject root = Read(config.FilePath) ?? new JsonObject();
        if (!exists && Read(Path.Combine(dir, LegacyFileName)) is { } legacy)
        {
            foreach (var key in LegacyKeys)
                if (legacy[key] is JsonValue value) root[key] = value.DeepClone();
            Log.Info($"carried the chat settings over from {LegacyFileName}");
        }

        if (root["port"] is JsonValue p && p.TryGetValue(out int port) && port is > 0 and <= 65535)
            config.Port = port;
        if (root["token"] is JsonValue t && t.TryGetValue(out string? token) && !string.IsNullOrWhiteSpace(token))
            config.Token = token;
        if (root["fast_mode"] is JsonValue f && f.TryGetValue(out string? fast) && fast is "instant" or "keep")
            config.FastMode = fast;
        if (root["capture_dir"] is JsonValue c && c.TryGetValue(out string? captureDir) && !string.IsNullOrWhiteSpace(captureDir))
            config.CaptureDir = captureDir;
        if (root["chat_width"] is JsonValue cw && cw.TryGetValue(out double chatWidth) && chatWidth > 0)
            config.ChatWidth = (float)chatWidth;
        if (root["chat_agent"] is JsonValue ag && ag.TryGetValue(out string? agent) && agent is "claude" or "codex")
            config.ChatAgent = agent;
        if (root["chat_auto_connect"] is JsonValue ac && ac.TryGetValue(out bool autoConnect))
            config.ChatAutoConnect = autoConnect;
        if (root["chat_collapsed"] is JsonValue cc && cc.TryGetValue(out bool collapsed))
            config.ChatCollapsed = collapsed;
        if (root["chat_play_level"] is JsonValue pl && pl.TryGetValue(out string? level) && level is "commentary" or "colow" or "cohigh" or "auto")
            config.ChatPlayLevel = level;
        if (root["claude_path"] is JsonValue cl && cl.TryGetValue(out string? claudePath) && !string.IsNullOrWhiteSpace(claudePath))
            config.ClaudePath = claudePath;

        if (string.IsNullOrEmpty(config.Token)) config.Token = NewToken();
        if (!exists || root["token"] is null)
        {
            root["port"] = config.Port;
            root["token"] = config.Token;
            root["fast_mode"] = config.FastMode;
            try
            {
                File.WriteAllText(config.FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Log.Info($"wrote config {config.FilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"could not write {config.FilePath}: {ex.Message} (token is valid for this session only)");
            }
        }

        return config;
    }

    private static JsonObject? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex)
        {
            Log.Error($"failed to read {path}: {ex.Message}; using defaults");
            return null;
        }
    }

    /// <summary>Remembers the chat panel width (written to the config file, other keys kept as they are).</summary>
    public void SaveChatWidth(float width)
    {
        ChatWidth = width;
        Save("chat_width", Math.Round(width));
    }

    /// <summary>Remembers whether the chat is folded away.</summary>
    public void SaveChatCollapsed(bool collapsed)
    {
        ChatCollapsed = collapsed;
        Save("chat_collapsed", collapsed);
    }

    /// <summary>Remembers "connect automatically".</summary>
    public void SaveChatAutoConnect(bool on)
    {
        ChatAutoConnect = on;
        Save("chat_auto_connect", on);
    }

    /// <summary>Remembers which agent card the player opened last.</summary>
    public void SaveChatAgent(string agent)
    {
        ChatAgent = agent;
        Save("chat_agent", agent);
    }

    /// <summary>Remembers the chat's play level.</summary>
    public void SaveChatPlayLevel(string level)
    {
        ChatPlayLevel = level;
        Save("chat_play_level", level);
    }

    /// <summary>Writes one key to the config file, keeping the others as they are.</summary>
    private void Save(string key, JsonNode? value)
    {
        if (FilePath == null) return;
        try
        {
            var root = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject() : new JsonObject();
            root[key] = value;
            File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"could not save {key}: {ex.Message}");
        }
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
