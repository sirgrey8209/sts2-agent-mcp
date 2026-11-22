using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2AgentMcp.Host;

/// <summary>
/// STS2AgentMcp.conf next to the DLL. Created on first run with a random bearer token.
/// (The in-game chat is a separate mod with its own STS2AgentChat.conf; chat_* keys left here are ignored.)
/// </summary>
internal sealed class Config
{
    public const string FileName = "STS2AgentMcp.conf";

    public int Port { get; private set; } = 15527;
    public string Token { get; private set; } = "";
    /// <summary>"instant" (default) forces FastMode=Instant at startup; "keep" leaves the player's setting.</summary>
    public string FastMode { get; private set; } = "instant";
    /// <summary>Where screenshots and card sheets are written. Null = %TEMP%\sts2-agent-mcp.</summary>
    public string? CaptureDir { get; private set; }
    /// <summary>Registers development tools (dev_console: the game's cheat console). Off by default.</summary>
    public bool DevTools { get; private set; }

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
        JsonObject root;
        try
        {
            root = File.Exists(config.FilePath)
                ? JsonNode.Parse(File.ReadAllText(config.FilePath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (Exception ex)
        {
            Log.Error($"failed to read {config.FilePath}: {ex.Message}; using defaults");
            root = new JsonObject();
        }

        if (root["port"] is JsonValue p && p.TryGetValue(out int port) && port is > 0 and <= 65535)
            config.Port = port;
        if (root["token"] is JsonValue t && t.TryGetValue(out string? token) && !string.IsNullOrWhiteSpace(token))
            config.Token = token;
        if (root["fast_mode"] is JsonValue f && f.TryGetValue(out string? fast) && fast is "instant" or "keep")
            config.FastMode = fast;
        if (root["capture_dir"] is JsonValue c && c.TryGetValue(out string? captureDir) && !string.IsNullOrWhiteSpace(captureDir))
            config.CaptureDir = captureDir;
        if (root["dev_tools"] is JsonValue dt && dt.TryGetValue(out bool devTools))
            config.DevTools = devTools;

        bool changed = false;
        if (string.IsNullOrEmpty(config.Token))
        {
            config.Token = NewToken();
            changed = true;
        }

        if (changed || !File.Exists(config.FilePath))
        {
            var output = new JsonObject
            {
                ["port"] = config.Port,
                ["token"] = config.Token,
                ["fast_mode"] = config.FastMode,
            };
            try
            {
                File.WriteAllText(config.FilePath, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Log.Info($"wrote config {config.FilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"could not write {config.FilePath}: {ex.Message} (token is valid for this session only)");
            }
        }

        return config;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
