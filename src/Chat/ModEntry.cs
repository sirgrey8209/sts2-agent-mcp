using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Modding;
using Sts2AgentMcp.Chat;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Session;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Host;

/// <summary>
/// STS2AgentChat: the in-game chat (panel on the left, the game on the right, Claude Code sessions) with its own MCP
/// endpoints on its own port (15528): <see cref="ChatPath"/> for the chat's agents, <see cref="DevPath"/> for
/// development. Runs alone or beside STS2AgentMcp; then that mod's outside clients come first (<see cref="Bridge"/>).
/// </summary>
[ModInitializer("Initialize")]
public static class ModEntry
{
    /// <summary>The chat's Claude Code sessions connect here.</summary>
    public const string ChatPath = "/mcp/chat";
    /// <summary>Development: drives and inspects the chat (layout, auth card, screenshots).</summary>
    public const string DevPath = "/mcp/dev";

    private static HttpHost? _host;

    public static void Initialize()
    {
        try
        {
            var config = Config.Load();
            MainThread.Install();

            // Tell the other mod first: from now on it takes card selections only while its own client acts.
            Bridge.ChatPresent = true;
            AgentLock.WatchExternalActs();

            Startup.CheckGameVersion();
            Startup.InstallGameHooks();
            ChatLayout.Start(config);
            if (config.FastMode == "instant") Startup.ApplyInstantModeWhenReady();
            Capture.Configure(config.CaptureDir);

            var runner = new ActRunner();
            // The chat's tools (docs/chat-tools.md). operate is the chat's act: not an outside client's, and checked
            // against the chat's rules first.
            ChatSessions.Runner = runner;
            var chatTools = new ToolRegistry()
                .Add(new LookTool(runner))
                .Add(new InfoTool())
                .Add(new InfoGrepTool())
                .Add(new OpenTool())
                .Add(new MarkTool())
                .Add(new ActTool(runner, external: false, guard: ChatSessions.ActRefusal, name: "operate"))
                .Add(new WaitTool())
                .Add(new NotesTool());
            var devTools = new ToolRegistry()
                .Add(new StateTool(runner))
                .Add(new ActTool(runner, external: false))
                .Add(new InfoTool())
                .Add(new ScreenshotTool())
                .Add(new RenderCardsTool())
                .Add(new RenderMapTool())
                .Add(new DevLayoutTool());

            _host = new HttpHost(config.Port, config.Token, new Dictionary<string, McpEndpoint>
            {
                [ChatPath] = new(chatTools),
                [DevPath] = new(devTools),
            });
            _host.Start();
            Log.Info($"ready (config: {config.FilePath})");
        }
        catch (Exception ex)
        {
            Log.Error($"failed to start: {ex}");
        }
    }
}
