using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Modding;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Session;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Host;

/// <summary>
/// STS2AgentMcp: the MCP endpoint for outside clients (POST /mcp, port 15527). No in-game chat: that is the separate
/// STS2AgentChat mod. When both run, an outside client's act comes first (<see cref="Bridge"/>).
/// </summary>
[ModInitializer("Initialize")]
public static class ModEntry
{
    private static HttpHost? _host;

    public static void Initialize()
    {
        try
        {
            var config = Config.Load();
            MainThread.Install();

            Startup.CheckGameVersion();
            Startup.InstallGameHooks();
            if (config.FastMode == "instant") Startup.ApplyInstantModeWhenReady();
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
                .Add(new ExportLocTool());
            if (config.DevTools) tools.Add(new DevConsoleTool());

            _host = new HttpHost(config.Port, config.Token, new Dictionary<string, McpEndpoint> { ["/mcp"] = new(tools) });
            _host.Start();
            Log.Info($"ready (config: {config.FilePath})");
        }
        catch (Exception ex)
        {
            Log.Error($"failed to start: {ex}");
        }
    }
}
