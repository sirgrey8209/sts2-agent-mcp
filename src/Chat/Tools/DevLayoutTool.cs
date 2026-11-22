using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Sts2AgentMcp.Chat;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Tools;

/// <summary>
/// Development tool for the in-game chat layout (docs/in-game-chat.md): inspect it, toggle it, and send synthetic
/// mouse/keyboard input through the root window (same transform path as OS input, without moving the real cursor).
/// Note: game code that reads the real cursor (Viewport.GetMousePosition: dragged cards, targeting arrow) still follows
/// the OS cursor, so drags cannot be simulated here.
/// </summary>
internal sealed class DevLayoutTool : ITool
{
    public string Name => "dev_layout";

    public string Description =>
        "Development (in-game chat layout). action: info | on | off | hover | move | click | drag | type | probe | say | agent | shot | auth. " +
        "agent: shows `text` as an agent message (markup preview, nothing is sent). ask: previews an AskUserQuestion card from `text` (its JSON arguments; the answer is not sent). " +
        "move/click take window pixel x, y; type takes text (ASCII letters, digits, space). " +
        "auth: login state and card; text \"recheck\" checks again, \"press:<label>\" presses a card or header button (\"새로고침\" for the refresh icon), \"popup:install|login|wait|console|connect|api\" previews a guidance popup.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("info", "on", "off", "hover", "move", "click", "drag", "type", "probe", "say", "agent", "ask", "shot", "auth") },
            ["x"] = new JsonObject { ["type"] = "number" },
            ["y"] = new JsonObject { ["type"] = "number" },
            ["x2"] = new JsonObject { ["type"] = "number", ["description"] = "drag: end x" },
            ["y2"] = new JsonObject { ["type"] = "number", ["description"] = "drag: end y" },
            ["text"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("action"),
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        string action = args["action"]?.GetValue<string>() ?? "info";
        var point = new Vector2(args["x"] is JsonValue xv ? (float)xv.GetValue<double>() : 0, args["y"] is JsonValue yv ? (float)yv.GetValue<double>() : 0);
        if (action == "click") return Click(point);
        if (action == "drag")
            return Drag(point, new Vector2(args["x2"] is JsonValue x2 ? (float)x2.GetValue<double>() : point.X,
                                           args["y2"] is JsonValue y2 ? (float)y2.GetValue<double>() : point.Y));
        if (action == "type") return Type(args["text"]?.GetValue<string>() ?? "");
        if (action == "probe") return MainThread.Run<ToolOutput>(() => ScenePeek.Dump(args["text"]?.GetValue<string>() ?? ""));
        if (action == "say")
            return MainThread.Run<ToolOutput>(() =>
            {
                if (ChatLayout.Panel == null) throw new ToolError("chat panel is off");
                ChatLayout.Panel.Submit(args["text"]?.GetValue<string>() ?? "");
                return "sent";
            });
        if (action == "agent")
            return MainThread.Run<ToolOutput>(() =>
            {
                if (ChatLayout.Panel == null) throw new ToolError("chat panel is off");
                // Shows text as an agent message (markup preview); nothing is sent to any session.
                var markup = Markup.Parse(args["text"]?.GetValue<string>() ?? "");
                ChatLayout.Panel.AddAgent(markup);
                return $"shown: {markup.Tips.Count} name(s): {markup.Plain}";
            });
        if (action == "ask")
            return MainThread.Run<ToolOutput>(() =>
            {
                if (ChatLayout.Panel == null) throw new ToolError("chat panel is off");
                // Previews an AskUserQuestion card from `text` (its JSON arguments); the answer only shows as a bubble.
                var questions = AskUserQuestionTool.Parse(System.Text.Json.Nodes.JsonNode.Parse(args["text"]?.GetValue<string>() ?? "{}") as JsonObject ?? new JsonObject());
                var panel = ChatLayout.Panel;
                panel.AddQuestions(questions, (text, _) => Host.Log.Info($"dev ask answered: {text.Replace('\n', '/')}"), null);
                return $"shown {questions.Count} question(s)";
            });
        if (action == "shot") return MainThread.Run<ToolOutput>(() => "whole window: " + Capture.Screenshot(0, wholeWindow: true).Path);
        if (action == "auth") return MainThread.Run<ToolOutput>(() => Auth(args["text"]?.GetValue<string>() ?? ""));

        return MainThread.Run<ToolOutput>(() => action switch
        {
            "info" => Info(),
            "on" => Run(ChatLayout.Enable),
            "off" => Run(ChatLayout.Disable),
            "hover" => Hover(),
            "move" => Move(point),
            _ => throw new ToolError($"unknown action {action}"),
        });
    }

    private static Window RootWindow => (Window)Ui.Root;

    private static string Run(System.Action action)
    {
        action();
        return Info();
    }

    private static string Info()
    {
        var w = RootWindow;
        var sb = new StringBuilder();
        sb.AppendLine($"chat layout: {(ChatLayout.Enabled ? "on" : "off")} · chat width {ChatLayout.ChatWidth:0} units · game rect (px) {ChatLayout.GameRectPx} · mouse remap {(MouseRemap.Installed ? "installed" : "missing")}");
        sb.AppendLine($"window: pos {DisplayServer.WindowGetPosition()} size {DisplayServer.WindowGetSize()} mode {DisplayServer.WindowGetMode()}");
        sb.AppendLine($"root: size {w.Size} content_scale {w.ContentScaleSize} aspect {w.ContentScaleAspect} visible_rect {w.GetVisibleRect()}");
        sb.AppendLine($"canvas_transform {Fmt(w.CanvasTransform)} final_transform {Fmt(w.GetFinalTransform())}");
        sb.AppendLine("canvas layers (root viewport):");
        foreach (var layer in Ui.FindAll<CanvasLayer>(w).Where(l => l.GetViewport() == w))
            sb.AppendLine($"  L{layer.Layer} {Path(layer)} visible={layer.Visible} xf={Fmt(layer.Transform)}");
        return sb.ToString();
    }

    /// <summary>Login state and card (§3.1). text: "" shows it, "recheck" checks again, "press:&lt;label&gt;" presses a card/header button.</summary>
    private static string Auth(string text)
    {
        var panel = ChatLayout.Panel ?? throw new ToolError("chat panel is off");
        if (text == "recheck") ChatController.Recheck();
        else if (text.StartsWith("press:"))
        {
            string label = text["press:".Length..];
            if (!panel.Press(label)) throw new ToolError($"no visible button \"{label}\"");
        }
        else if (text.StartsWith("popup:"))
        {
            string which = text["popup:".Length..];
            if (which is not ("install" or "login" or "wait" or "console" or "connect" or "api")) throw new ToolError("popup:install|login|wait|console|connect|api");
            panel.PreviewPopup(which);
        }
        else if (text.Length > 0) throw new ToolError("text: \"\" | recheck | press:<label> | popup:install|login|wait|console|connect|api");
        var (primary, secondary) = panel.CardButtons;
        return $"auth: {ChatController.Auth}\nsetting up: {ChatController.SettingUp ?? "-"}\n" +
               $"card: {(panel.CardVisible ? "shown" : "hidden")} buttons [{primary ?? "-"}] [{secondary ?? "-"}]\nnote: {panel.CardNote}\n{panel.CardDebug}\n" +
               $"config dir: {ClaudeCli.ConfigDir}";
    }

    private static string Move(Vector2 p)
    {
        RootWindow.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        return Hover();
    }

    private static async Task<ToolOutput> Click(Vector2 p)
    {
        string before = await MainThread.Run(() => Move(p));
        await Task.Delay(100);
        await MainThread.Run(() => RootWindow.PushInput(new InputEventMouseButton
            { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }));
        await Task.Delay(80);
        await MainThread.Run(() => RootWindow.PushInput(new InputEventMouseButton
            { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }));
        return "clicked; before click: " + before;
    }

    private static async Task<ToolOutput> Drag(Vector2 from, Vector2 to)
    {
        await MainThread.Run(() => Move(from));
        await MainThread.Run(() => RootWindow.PushInput(new InputEventMouseButton
            { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }));
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            var p = from.Lerp(to, i / (float)steps);
            // Relative too: the map scrolls by the motion's relative movement.
            var relative = (to - from) / steps;
            await MainThread.Run(() => RootWindow.PushInput(new InputEventMouseMotion
                { Position = p, GlobalPosition = p, Relative = relative, ButtonMask = MouseButtonMask.Left }));
            await Task.Delay(30);
        }
        await MainThread.Run(() => RootWindow.PushInput(new InputEventMouseButton
            { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false }));
        await Task.Delay(100);
        return await MainThread.Run(Info);
    }

    private static async Task<ToolOutput> Type(string text)
    {
        if (text == "ctrl+c")
        {
            foreach (bool pressed in new[] { true, false })
            {
                await MainThread.Run(() => RootWindow.PushInput(new InputEventKey
                    { Keycode = Key.C, PhysicalKeycode = Key.C, CtrlPressed = true, Pressed = pressed }));
                await Task.Delay(60);
            }
            return await MainThread.Run(() => $"pressed ctrl+c; clipboard: \"{DisplayServer.ClipboardGet()}\" ({DisplayServer.ClipboardGet().Count(c => c == '⁠')} word joiners)");
        }
        foreach (char ch in text)
        {
            // "\n" presses Enter, "\r" Shift+Enter.
            Key key = char.IsLetter(ch) ? Key.A + (char.ToUpperInvariant(ch) - 'A')
                : char.IsDigit(ch) ? Key.Key0 + (ch - '0')
                : ch == ' ' ? Key.Space : ch is '\n' or '\r' ? Key.Enter : Key.None;
            long unicode = ch is '\n' or '\r' ? 0 : ch;
            bool shift = ch == '\r';
            foreach (bool pressed in new[] { true, false })
            {
                await MainThread.Run(() => RootWindow.PushInput(new InputEventKey
                    { Keycode = key, PhysicalKeycode = key, Unicode = pressed ? unicode : 0, Pressed = pressed, ShiftPressed = shift }));
                await Task.Delay(40);
            }
        }
        return await MainThread.Run(() =>
        {
            var focus = RootWindow.GuiGetFocusOwner();
            return $"typed {text.Length} chars; focus: {(focus == null ? "none" : Path(focus))}; panel text: \"{ChatLayout.Panel?.Input.Text}\"";
        });
    }

    private static string Hover()
    {
        var w = RootWindow;
        var control = w.GuiGetHoveredControl();
        string hovered = control == null ? "none" : $"{Path(control)} ({control.GetType().Name}) global_rect {control.GetGlobalRect()}";
        return $"mouse (game coords) {w.GetMousePosition()} screen {DisplayServer.MouseGetPosition()}\nhovered: {hovered}";
    }

    private static string Path(Node node)
    {
        string path = node.GetPath().ToString();
        return path.Length > 140 ? "…" + path[^139..] : path;
    }

    private static string Fmt(Transform2D t) =>
        t.IsEqualApprox(Transform2D.Identity) ? "identity" : $"[x {t.X} y {t.Y} o {t.Origin}]";
}
