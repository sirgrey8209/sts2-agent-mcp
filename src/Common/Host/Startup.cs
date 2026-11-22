using System;
using System.IO;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Host;

/// <summary>Start-up steps both mods take (each mod runs them for itself; doing them twice is harmless).</summary>
internal static class Startup
{
    /// <summary>Game version this build was written against (release_info.json "version").</summary>
    public const string TargetGameVersion = "v0.111.1";

    /// <summary>The game's card-selection hook, event log and turn tracking (shared by every tool).</summary>
    public static void InstallGameHooks()
    {
        Features.Try("choice-broker", ChoiceBroker.Install);
        Features.Try("event-log", EventLog.Install);
        Features.Try("turn-tracker", TurnTracker.Install);
    }

    /// <summary>
    /// Mods initialize before the game loads its prefs, and the game resets Instant to Fast while loading them.
    /// So wait until the game has finished booting (main menu or a run exists), then apply.
    /// </summary>
    public static void ApplyInstantModeWhenReady()
    {
        string? Step()
        {
            bool done = false;
            Features.Try("fast-mode", () => done = TrySetInstantMode());
            return done || !Features.IsEnabled("fast-mode") ? "done" : null;
        }
        _ = MainThread.Poll(Step, TimeSpan.FromMinutes(10), () => "timeout");
    }

    private static bool TrySetInstantMode()
    {
        if (NGame.Instance?.MainMenu == null && !RunManager.Instance.IsInProgress) return false;
        var prefs = SaveManager.Instance.PrefsSave;
        if (prefs == null) return false;
        if (prefs.FastMode != FastModeType.Instant)
        {
            prefs.FastMode = FastModeType.Instant;
            Log.Info("fast mode set to Instant");
        }
        return true;
    }

    public static void CheckGameVersion()
    {
        try
        {
            string dir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            string file = Path.Combine(dir, "release_info.json");
            if (!File.Exists(file)) file = Path.Combine(dir, "..", "Resources", "release_info.json"); // macOS app bundle
            if (!File.Exists(file)) return;
            string? version = (JsonNode.Parse(File.ReadAllText(file))?["version"] as JsonValue)?.GetValue<string>();
            KbDocs.GameVersion = version;
            if (version != null && version != TargetGameVersion)
                Log.Warn($"game version {version} differs from target {TargetGameVersion}; incompatible features will be disabled individually");
        }
        catch (Exception ex)
        {
            Log.Warn($"could not read game version: {ex.Message}");
        }
    }
}
