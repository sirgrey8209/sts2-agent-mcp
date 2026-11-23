using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Game;

/// <summary>One mark of the <c>mark</c> tool: what it is, what it points at, how it looks, and what pressing it does.</summary>
/// <param name="Kind">badge, circle, arrow (two targets: from, to) or line (targets in order).</param>
/// <param name="Click">null (not pressable), "answer" (the label goes to the agent as the player's pick) or "operate"
/// (the actions run, as if the player did them).</param>
internal sealed record MarkItem(string Kind, IReadOnlyList<string> Targets, string? Label, Color? Color, string? Click,
    IReadOnlyList<string> Actions);

/// <summary>
/// The chat agent's marks over the game: badges, hand-drawn circles, arrows and lines in its ink colors, pointing at
/// screen elements by <see cref="ScreenTargets"/> ids. They live in a canvas layer of their own over the game (the chat
/// layout gives it the game's transform), follow their targets every frame (cards move, creatures die: a mark whose
/// target is gone hides), and go when the agent draws again, clears them, or the turn or room changes. Marks on map
/// nodes are drawn into the map instead (<see cref="MapInk"/>). A badge can be pressed (<see cref="Clicked"/>).
/// Hints are the same marks shown only while the player hovers a plan in the chat's drawer. Main thread only.
/// </summary>
internal static class ScreenMarks
{
    private const string LayerName = "AgentMarks";
    private const float LineWidth = 5f;

    private sealed class Live
    {
        public required Func<Rect2?>[] Locators;
        public required Node2D Node;
        public required Action<Node2D, Rect2[]> Build;
        public Rect2[]? Last;
    }

    private static CanvasLayer? _layer;
    private static Node2D? _marks;
    private static Node2D? _hints;
    private static readonly List<Live> Marks = new();
    private static readonly List<Live> Hints = new();
    private static bool _hooked;

    /// <summary>A pressable mark was pressed: its label, "answer" or "operate", and its actions.</summary>
    public static event Action<string, string, IReadOnlyList<string>>? Clicked;

    /// <summary>Draws the marks (replacing the earlier ones unless <paramref name="keep"/>); a summary for the agent.</summary>
    public static string Draw(IReadOnlyList<MarkItem> items, bool keep)
    {
        if (!keep) Clear();
        var summary = new List<string>();
        var mapRoutes = new List<InkRoute>();
        var mapMarks = new List<InkMark>();
        char nextLetter = 'A';
        int colorIndex = 0;
        foreach (var item in items)
        {
            if (item.Targets.Count == 0) throw new ToolError($"{item.Kind}: give a target");
            var color = item.Color ?? (colorIndex < MapInk.RouteColors.Length ? MapInk.RouteColors[colorIndex] : MapInk.MarkColor);
            colorIndex++;
            string? label = item.Label ?? (item.Kind == "badge" ? (nextLetter++).ToString() : null);

            if (item.Targets.All(ScreenTargets.IsMapNode))
            {
                if (item.Kind == "line") mapRoutes.Add(new InkRoute(item.Targets, color, label));
                else foreach (var node in item.Targets) mapMarks.Add(new InkMark(node, color, label));
                continue;
            }

            var locators = new List<Func<Rect2?>>();
            foreach (var target in item.Targets)
                locators.Add(ScreenTargets.Locate(target, out string? error) ?? throw new ToolError(error!));
            var layer = Layer();
            var live = Add(Marks, _marks!, locators.ToArray(), Builder(item, color, label));
            summary.Add($"{item.Kind}{(label != null ? " " + label : "")} ({MapInk.ColorName(color)}): {string.Join(" > ", item.Targets)}" +
                        (item.Click != null ? $", press: {item.Click}" : ""));
            Refresh(live);
        }
        if (mapRoutes.Count > 0 || mapMarks.Count > 0) summary.Add(MapInk.Draw(mapRoutes, mapMarks));
        return summary.Count == 0 ? "nothing drawn" : "drew: " + string.Join("; ", summary);
    }

    /// <summary>Removes every mark (and the agent's ink on the map, if the map exists).</summary>
    public static void Clear()
    {
        foreach (var live in Marks) live.Node.QueueFree();
        Marks.Clear();
        if (MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance is { } map) MapInk.Clear(map);
    }

    /// <summary>Arrows shown while the player hovers a plan (from, to pairs), in the plan's color.</summary>
    public static void ShowHint(IReadOnlyList<(string From, string To)> arrows, Color color)
    {
        ClearHint();
        Layer();
        foreach (var (from, to) in arrows)
        {
            var a = ScreenTargets.Locate(from, out _);
            var b = ScreenTargets.Locate(to, out _);
            if (a == null || b == null) continue;
            Refresh(Add(Hints, _hints!, new[] { a, b }, Builder(new MarkItem("arrow", new[] { from, to }, null, color, null, Array.Empty<string>()), color, null)));
        }
    }

    public static void ClearHint()
    {
        foreach (var live in Hints) live.Node.QueueFree();
        Hints.Clear();
    }

    // ---- layer and per-frame follow ----

    private static CanvasLayer Layer()
    {
        if (_layer != null && Ui.IsLive(_layer)) return _layer;
        // Over the game's own layers, under the chat panel (120). The chat layout gives it the game's transform.
        _layer = new CanvasLayer { Name = LayerName, Layer = 100 };
        _marks = new Node2D { Name = "Marks" };
        _hints = new Node2D { Name = "Hints" };
        _layer.AddChild(_marks);
        _layer.AddChild(_hints);
        Ui.Root.AddChild(_layer);
        if (!_hooked)
        {
            ((SceneTree)Engine.GetMainLoop()).Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Follow));
            _hooked = true;
        }
        return _layer;
    }

    private static Live Add(List<Live> list, Node2D parent, Func<Rect2?>[] locators, Action<Node2D, Rect2[]> build)
    {
        var node = new Node2D();
        parent.AddChild(node);
        var live = new Live { Locators = locators, Node = node, Build = build };
        list.Add(live);
        return live;
    }

    private static void Follow()
    {
        try
        {
            foreach (var live in Marks) Refresh(live);
            foreach (var live in Hints) Refresh(live);
        }
        catch (Exception ex) { Log.Warn($"marks: follow failed: {ex.Message}"); }
    }

    /// <summary>Hides a mark whose target is off screen; draws it again when a target moved.</summary>
    private static void Refresh(Live live)
    {
        if (!Ui.IsLive(live.Node)) return;
        var rects = new Rect2[live.Locators.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            if (live.Locators[i]() is not { } r) { live.Node.Visible = false; return; }
            rects[i] = r;
        }
        live.Node.Visible = true;
        if (live.Last != null && live.Last.Zip(rects, (a, b) => a.Position.DistanceTo(b.Position) < 1 && a.Size.DistanceTo(b.Size) < 1).All(x => x)) return;
        live.Last = rects;
        foreach (var child in live.Node.GetChildren()) child.QueueFree();
        live.Build(live.Node, rects);
    }

    // ---- shapes ----

    private static Action<Node2D, Rect2[]> Builder(MarkItem item, Color color, string? label)
    {
        int seed = MapInk.Seed(string.Join(",", item.Targets), item.Kind, label?.GetHashCode() ?? 0);
        return item.Kind switch
        {
            "badge" => (node, rects) =>
            {
                foreach (var r in rects) node.AddChild(Badge(label ?? "", color, BadgeSpot(r), item, seed));
            },
            "circle" => (node, rects) =>
            {
                foreach (var r in rects)
                {
                    node.AddChild(Stroke(MapInk.Circle(r.GetCenter(), r.Size.X / 2 + 12, r.Size.Y / 2 + 12, seed), color));
                    if (label != null) node.AddChild(Badge(label, color, BadgeSpot(r.Grow(12)), item, seed));
                }
            },
            "arrow" => (node, rects) =>
            {
                if (rects.Length < 2) return;
                var points = MapInk.Segment(rects[0].GetCenter(), rects[^1].GetCenter(), seed, 30f);
                if (points.Count < 2) return;
                node.AddChild(Stroke(points, color));
                node.AddChild(Head(points[^2], points[^1], color));
                if (label != null) node.AddChild(Badge(label, color, points[points.Count / 2] + new Vector2(0, -24), item, seed));
            },
            "line" => (node, rects) =>
            {
                for (int i = 0; i + 1 < rects.Length; i++)
                    node.AddChild(Stroke(MapInk.Segment(rects[i].GetCenter(), rects[i + 1].GetCenter(), seed + i, 24f), color));
                if (label != null && rects.Length > 1)
                    node.AddChild(Badge(label, color, (rects[0].GetCenter() + rects[1].GetCenter()) / 2 + new Vector2(0, -26), item, seed));
            },
            _ => throw new ToolError($"unknown mark kind '{item.Kind}' (badge, circle, arrow, line, combat_plan, overlay, clear)"),
        };
    }

    /// <summary>A badge sits at the target's top-right corner, a little outside.</summary>
    private static Vector2 BadgeSpot(Rect2 r) => new(r.End.X - 4, r.Position.Y + 4);

    private static Line2D Stroke(List<Vector2> points, Color color) => new()
    {
        Points = points.ToArray(),
        Width = LineWidth,
        DefaultColor = color,
        JointMode = Line2D.LineJointMode.Round,
        BeginCapMode = Line2D.LineCapMode.Round,
        EndCapMode = Line2D.LineCapMode.Round,
        Antialiased = true,
    };

    private static Polygon2D Head(Vector2 before, Vector2 tip, Color color)
    {
        var dir = (tip - before).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        const float length = 22, width = 12;
        return new Polygon2D
        {
            Polygon = new[] { tip + dir * 4, tip - dir * length + side * width, tip - dir * length - side * width },
            Color = color,
            Antialiased = true,
        };
    }

    private static Node2D Badge(string text, Color color, Vector2 at, MarkItem item, int seed)
    {
        var badge = InkBadge.Create(text, color, seed);
        badge.Position = at;
        if (item.Click is { } click && text.Length > 0)
        {
            // A pressable badge: a round hit area over the disc.
            var hit = new Button
            {
                Flat = true,
                Position = new Vector2(-InkBadge.Radius, -InkBadge.Radius),
                Size = new Vector2(InkBadge.Radius * 2, InkBadge.Radius * 2),
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                FocusMode = Control.FocusModeEnum.None,
            };
            hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            hit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            hit.MouseEntered += () => badge.Scale = new Vector2(1.15f, 1.15f);
            hit.MouseExited += () => badge.Scale = Vector2.One;
            hit.Pressed += () => Clicked?.Invoke(text, click, item.Actions);
            badge.AddChild(hit);
        }
        return badge;
    }
}
