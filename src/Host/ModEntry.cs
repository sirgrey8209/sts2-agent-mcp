using System;
using System.IO;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using Sts2AgentMcp.Chat;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Session;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Host;

[ModInitializer("Initialize")]
public static class ModEntry
{
    /// <summary>Game version this build was written against (release_info.json "version").</summary>
    private const string TargetGameVersion = "v0.111.1";

    private static HttpHost? _host;

    public static void Initialize()
    {
        try
        {
            var config = Config.Load();
            MainThread.Install();

            CheckGameVersion();
            Features.Try("choice-broker", ChoiceBroker.Install);
            Features.Try("event-log", EventLog.Install);
            Features.Try("turn-tracker", TurnTracker.Install);
            if (config.ChatPanel) ChatLayout.Start(config);
            if (config.FastMode == "instant") ApplyInstantModeWhenReady();
            Capture.Configure(config.CaptureDir);

            var runner = new ActRunner();
            var tools = new ToolRegistry()
                .Add(new StateTool(runner))
                .Add(new ActTool(runner))
                .Add(new InfoTool())
                .Add(new SearchTool())
                .Add(new ScreenshotTool())
                .Add(new RenderCardsTool())
                .Add(new RenderMapTool())
                .Add(new ExportLocTool())
                .Add(new DevLayoutTool());
            if (config.DevTools) tools.Add(new DevConsoleTool());

            _host = new HttpHost(config, new McpEndpoint(tools));
            _host.Start();
            Log.Info($"ready (config: {config.FilePath})");
        }
        catch (Exception ex)
        {
            Log.Error($"failed to start: {ex}");
        }
    }

    /// <summary>
    /// Mods initialize before the game loads its prefs, and the game resets Instant to Fast while loading them.
    /// So wait until the game has finished booting (main menu or a run exists), then apply.
    /// </summary>
    private static void ApplyInstantModeWhenReady()
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

    private static void CheckGameVersion()
    {
        try
        {
            string dir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            string file = Path.Combine(dir, "release_info.json");
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
