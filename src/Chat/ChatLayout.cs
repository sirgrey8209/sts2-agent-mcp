using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// In-game chat layout (docs/in-game-chat.md): a chat panel of fixed width on the left, the game at full window height
/// in the rest of the window. The game lays itself out for that area's aspect ratio, as it would for a window of that shape.
///
/// How: the root window's content size is set to what the game's "auto" aspect logic would pick for the game area's
/// ratio, with aspect "ignore", so the game lays out for that area while the root still renders to the whole window.
/// A canvas transform (on the default canvas and on every game CanvasLayer of the root viewport) undoes the stretch and
/// pins the game to the right edge. GUI input follows those transforms; <see cref="MouseRemap"/> fixes
/// Viewport.GetMousePosition, which does not.
/// </summary>
internal static class ChatLayout
{
    /// <summary>Chat width limits, in units of a 1080-tall window.</summary>
    public const float MinChatWidth = 240;
    /// <summary>The game area is never narrower than this aspect ratio (below 4:3 the game uses its narrow-window layout).</summary>
    public const float MinGameAspect = 1.0f;

    private static readonly Dictionary<ulong, (CanvasLayer Layer, Transform2D Original)> Layers = new();
    private static Config? _config;
    private static float _chatWidth = 480;
    private static Vector2I _content;
    private static bool _resizing;
    private static readonly List<CanvasLayer> PendingLayers = new();
    private static (Vector2I Size, Window.ContentScaleAspectEnum Aspect)? _savedContent;
    private static Transform2D _xf = Transform2D.Identity;
    private static Vector2I _appliedWindow;
    private static bool _dirty;
    private static bool _hooked;
    private static ChatPanel? _panel;

    public static bool Enabled { get; private set; }

    /// <summary>Where the game is drawn, in window pixels (screenshots crop to this).</summary>
    public static Rect2I GameRectPx { get; private set; }

    public static ChatPanel? Panel => _panel;

    private static Window Root => (Window)Ui.Root;

    /// <summary>Chat panel width in units of a 1080-tall window.</summary>
    public static float ChatWidth => _chatWidth;

    /// <summary>Mod init: enables the layout as soon as the game node is ready (before the main menu shows).</summary>
    public static void Start(Config config)
    {
        _config = config;
        _chatWidth = config.ChatWidth;
        // Screenshots of both mods crop to the game (the panel is not part of what an agent should see).
        Bridge.SetGameRect(() => Enabled ? new[] { GameRectPx.Position.X, GameRectPx.Position.Y, GameRectPx.Size.X, GameRectPx.Size.Y } : null);
        _ = MainThread.Poll(() =>
        {
            if (NGame.Instance == null || !NGame.Instance.IsNodeReady()) return null;
            Enable();
            return "done";
        }, TimeSpan.FromMinutes(5), () => "timeout");
    }

    /// <summary>Main thread.</summary>
    public static void Enable()
    {
        if (Enabled) return;
        if (!MouseRemap.Install()) Log.Warn("chat layout: mouse remap unavailable; dragged cards will be offset");
        if (!Features.Try("chat-layout", Hook)) return;
        Enabled = true;
        Relayout();
    }

    /// <summary>Main thread. Restores the game's own layout.</summary>
    public static void Disable()
    {
        if (!Enabled) return;
        Enabled = false;
        var w = Root;
        w.CanvasTransform = Transform2D.Identity;
        foreach (var (layer, original) in Layers.Values)
            if (Ui.IsLive(layer)) layer.Transform = original;
        Layers.Clear();
        PendingLayers.Clear();
        MouseRemap.Deactivate();
        _panel?.SetVisible(false);
        if (_savedContent is { } saved)
        {
            w.ContentScaleAspect = saved.Aspect;
            w.ContentScaleSize = saved.Size;
            _savedContent = null;
        }
        GameRectPx = new Rect2I(Vector2I.Zero, w.Size);
    }

    private static void Hook()
    {
        if (_hooked) return;
        var harmony = new Harmony("sirgrey8209.STS2AgentChat");
        // The game's "auto" aspect handlers would reset the content size on every window change and fight the layout.
        var skip = new HarmonyMethod(typeof(ChatLayout), nameof(SkipWhileEnabled));
        harmony.Patch(Required(AccessTools.Method(typeof(NGlobalUi), "OnWindowChange")), prefix: skip);
        harmony.Patch(Required(AccessTools.Method(typeof(NMainMenu), "OnWindowChange")), prefix: skip);
        // Display settings (aspect ratio option, startup) set a content size; lay out again afterwards.
        harmony.Patch(Required(AccessTools.Method(typeof(NGame), nameof(NGame.ApplyDisplaySettings))),
            postfix: new HarmonyMethod(typeof(ChatLayout), nameof(MarkDirty)));

        var tree = (SceneTree)Engine.GetMainLoop();
        tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Tick));
        tree.Connect(SceneTree.SignalName.NodeAdded, Callable.From<Node>(OnNodeAdded));
        _hooked = true;
    }

    private static System.Reflection.MethodInfo Required(System.Reflection.MethodInfo? method) =>
        method ?? throw new MissingMethodException("chat layout: game method to patch not found");

    private static bool SkipWhileEnabled() => !Enabled;

    private static void MarkDirty() => _dirty = true;

    private static void OnNodeAdded(Node node)
    {
        if (Enabled && node is CanvasLayer layer) PendingLayers.Add(layer);
    }

    private static void Tick()
    {
        if (!Enabled) return;
        try
        {
            var w = Root;
            if (_dirty || w.Size != _appliedWindow || w.ContentScaleSize != _content
                || w.ContentScaleAspect != Window.ContentScaleAspectEnum.Ignore)
            {
                Relayout();
                return;
            }
            if (PendingLayers.Count > 0)
            {
                foreach (var layer in PendingLayers) TransformLayer(w, layer);
                PendingLayers.Clear();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"chat layout tick failed, disabling: {ex}");
            Disable();
        }
    }

    /// <summary>Main thread. Live resize from the panel's divider (units of a 1080-tall window).</summary>
    public static void ResizeChat(float width)
    {
        _chatWidth = ClampChatWidth(width, (Vector2)Root.Size);
        _resizing = true;
        _dirty = true;
    }

    /// <summary>Main thread. Divider released: remember the width.</summary>
    public static void ResizeChatDone()
    {
        _resizing = false;
        _dirty = true;
        float width = _chatWidth;
        var config = _config;
        if (config != null) System.Threading.Tasks.Task.Run(() => config.SaveChatWidth(width));
    }

    private static float ClampChatWidth(float width, Vector2 win)
    {
        float u = win.Y / 1080f;
        float max = Math.Max(MinChatWidth, (win.X - MinGameAspect * win.Y) / u);
        return Math.Clamp(width, MinChatWidth, max);
    }

    /// <summary>
    /// The content size the game's own "auto" aspect logic (NGlobalUi.OnWindowChange) would give a window of this
    /// aspect ratio: KeepWidth 2580x1080 above 21:9, otherwise Expand 1680x1080. Below 4:3 it stays 1680x1260 and the
    /// game is fitted to the area's width with bars above and below (Godot's keep_height): the game's screens place
    /// panels with fixed margins for at least 1680 units of width, and narrower content broke them (the early access
    /// panel, the profile cards, the map legend).
    /// </summary>
    private static Vector2I GameContentSize(float ratio)
    {
        static int R(float v) => (int)MathF.Round(v);
        if (ratio > 2.3888888f) return new Vector2I(2580, R(2580 / ratio));
        if (ratio < 4f / 3f) return new Vector2I(1680, 1260);
        if (ratio >= 1680f / 1080f) return new Vector2I(R(1080 * ratio), 1080);
        return new Vector2I(1680, R(1680 / ratio));
    }

    private static void Relayout()
    {
        var w = Root;
        var win = (Vector2)w.Size;
        if (win.X < 1 || win.Y < 1) return; // minimized
        float u = win.Y / 1080f;
        _chatWidth = ClampChatWidth(_chatWidth, win);
        float chatPx = MathF.Round(Math.Max(0, Math.Min(_chatWidth * u, win.X - MinGameAspect * win.Y)));
        var area = new Rect2(chatPx, 0, win.X - chatPx, win.Y);

        _content = GameContentSize(area.Size.X / area.Size.Y);
        _savedContent ??= (w.ContentScaleSize, w.ContentScaleAspect);
        w.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
        w.ContentScaleSize = _content;

        var stretch = w.GetFinalTransform();   // content units -> window pixels (global canvas transform is identity)
        float k = Math.Min(area.Size.Y / _content.Y, area.Size.X / _content.X);
        var size = k * (Vector2)_content;
        var target = new Vector2(win.X - size.X, (win.Y - size.Y) / 2);
        _xf = stretch.AffineInverse() * new Transform2D(0, new Vector2(k, k), 0, target);

        w.CanvasTransform = _xf;
        foreach (var layer in Ui.FindAll<CanvasLayer>(w)) TransformLayer(w, layer);
        PendingLayers.Clear();
        foreach (var id in Layers.Where(e => !Ui.IsLive(e.Value.Layer)).Select(e => e.Key).ToList()) Layers.Remove(id);
        MouseRemap.Activate(w, _xf, new Rect2(Vector2.Zero, _content));

        GameRectPx = new Rect2I((Vector2I)target.Round(), (Vector2I)size.Round());
        if (_panel == null)
        {
            _panel = new ChatPanel(w);
            if (_config != null) ChatController.Attach(_panel, _config);
        }
        _panel.Layout(stretch, win, new Rect2(target, size));
        _panel.SetVisible(true);

        _appliedWindow = w.Size;
        _dirty = false;
        if (!_resizing) Log.Info($"chat layout: window {w.Size}, chat {chatPx}px, game {GameRectPx} content {_content}");
    }

    private static void TransformLayer(Window root, CanvasLayer layer)
    {
        // Only layers drawn in the root viewport (not the map drawing or card-sheet SubViewports), and not our panel.
        if (!Ui.IsLive(layer) || layer.FollowViewportEnabled || layer.GetViewport() != root) return;
        if (_panel != null && layer == _panel.Layer) return;
        ulong id = layer.GetInstanceId();
        if (!Layers.TryGetValue(id, out var entry))
        {
            entry = (layer, layer.Transform);
            Layers[id] = entry;
        }
        layer.Transform = _xf * entry.Original;
    }
}
