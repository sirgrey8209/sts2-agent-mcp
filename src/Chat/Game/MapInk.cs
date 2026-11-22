using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Game;

/// <summary>A route to draw on the map: node references in order (r&lt;row&gt;c&lt;col&gt;, "current", "boss"), its ink, and
/// the badge that names it (default A, B, C).</summary>
internal sealed record InkRoute(IReadOnlyList<string> Nodes, Color? Color, string? Label = null);

/// <summary>A node to circle, with the badge that names it (default 1, 2, 3).</summary>
internal sealed record InkMark(string Node, Color? Color, string? Label = null);

/// <summary>
/// The chat agent's own ink on the game's map: routes and circled nodes drawn with the game's map quill (its line scene,
/// so width and texture match the player's drawings), in colors of its own, a little curved and shaky like a hand, and
/// stroke by stroke over a few frames. Each route and mark gets a badge in its own color (<see cref="InkBadge"/>), so
/// the chat can say "route A" or "node 2".
///
/// It is a layer of its own: a second instance of the game's per-player drawing scene (a SubViewport rendered half size
/// and shown as a texture) under the map's <see cref="NMapDrawings"/>, so it scrolls with the map. The game does not
/// know it: it is not saved with the run, not sent anywhere, and the player's eraser and clear button leave it alone.
/// The badges sit on top in a node of their own (the ink layer renders at half size, which would blur text).
/// Each call replaces what was drawn before. Main thread only.
/// </summary>
internal static class MapInk
{
    private const string LayerName = "AgentInk";
    private const string BadgesName = "AgentInkBadges";
    /// <summary>A route's badge sits this far beside its stroke (map units).</summary>
    private const float BadgeOffset = 34f;
    /// <summary>The drawing viewport is rendered at half the map's size (NMapDrawings adds points times 0.5).</summary>
    private const float ViewportScale = 0.5f;
    /// <summary>Strokes stop short of a node's center so its icon stays visible (map units).</summary>
    private const float NodeGap = 30f;
    private const float MarkRadius = 46f;
    /// <summary>Map units between two points of a stroke.</summary>
    private const float Step = 7f;
    /// <summary>Points added per frame while drawing (about one stroke segment in a third of a second at 60 fps).</summary>
    private const int PointsPerFrame = 3;

    private static readonly Color[] RouteColors =
    {
        new(0.16f, 0.36f, 0.66f), // blue ink
        new(0.17f, 0.50f, 0.30f), // green
        new(0.47f, 0.26f, 0.64f), // purple
    };
    private static readonly Color MarkColor = new(0.78f, 0.45f, 0.08f); // amber

    private static int _generation;

    /// <summary>Opens the map and draws the routes and marks, replacing earlier ink. Nothing given: clears it.</summary>
    public static string Draw(IReadOnlyList<InkRoute> routes, IReadOnlyList<InkMark> marks)
    {
        if (GameCtx.Capture().Run == null) throw new ToolError("no run in progress");
        var map = GameViews.OpenMap();
        int generation = ++_generation;
        Clear(map);
        if (routes.Count == 0 && marks.Count == 0) return "cleared the map ink";

        var points = GameViews.MapPoints(map);
        var drawings = map.Drawings ?? throw new ToolError("map drawings not ready");
        var inverse = drawings.GetGlobalTransform().AffineInverse();
        Vector2 Center(string reference)
        {
            var point = GameViews.Resolve(reference, points);
            return inverse * (point.GlobalPosition + point.Size * point.GetGlobalTransform().Scale / 2);
        }

        var strokes = new List<(Color Color, List<Vector2> Points)>();
        var badges = new List<(string Text, Color Color, Vector2 At)>();
        var summary = new List<string>();
        var resolved = routes.Select(r => r.Nodes.Select(Center).ToList()).ToList();
        for (int i = 0; i < routes.Count; i++)
        {
            var route = routes[i];
            if (route.Nodes.Count < 2) throw new ToolError("a route needs at least two nodes");
            var color = route.Color ?? RouteColors[i % RouteColors.Length];
            var centers = resolved[i];
            for (int s = 0; s + 1 < centers.Count; s++)
                strokes.Add((color, Segment(centers[s], centers[s + 1], Seed(route.Nodes[s], route.Nodes[s + 1], i))));
            string label = route.Label ?? ((char)('A' + i)).ToString();
            badges.Add((label, color, RouteBadgeAt(resolved, i)));
            summary.Add($"route {label} ({ColorName(color)}): {string.Join(" > ", route.Nodes)}");
        }
        for (int i = 0; i < marks.Count; i++)
        {
            var mark = marks[i];
            var color = mark.Color ?? MarkColor;
            var center = Center(mark.Node);
            strokes.Add((color, Circle(center, Seed(mark.Node, "mark", 0))));
            string label = mark.Label ?? (i + 1).ToString();
            badges.Add((label, color, center + new Vector2(MarkRadius * 0.85f, -MarkRadius * 0.85f)));
            summary.Add($"mark {label} ({ColorName(color)}): {mark.Node}");
        }

        void PlaceBadges()
        {
            if (generation != _generation || !Ui.IsLive(drawings)) return;
            var holder = new Node2D { Name = BadgesName, ZIndex = 10 };
            for (int i = 0; i < badges.Count; i++)
            {
                var badge = InkBadge.Create(badges[i].Text, badges[i].Color, Seed(badges[i].Text, "badge", i));
                badge.Position = badges[i].At;
                holder.AddChild(badge);
            }
            drawings.AddChild(holder);
        }

        if (!TryStartPen(drawings, strokes, generation, PlaceBadges))
        {
            DrawPlain(map, strokes);
            PlaceBadges();
        }
        return "drew on the map: " + string.Join("; ", summary);
    }

    /// <summary>
    /// Where a route's badge goes: beside the middle of its first segment that no other route shares (where the routes
    /// part), or of its middle segment when every segment is shared.
    /// </summary>
    private static Vector2 RouteBadgeAt(List<List<Vector2>> routes, int index)
    {
        static (Vector2, Vector2) Key(Vector2 a, Vector2 b) => a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? (a, b) : (b, a);
        var shared = new Dictionary<(Vector2, Vector2), int>();
        foreach (var route in routes)
            foreach (var key in route.Zip(route.Skip(1), Key).Distinct())
                shared[key] = shared.GetValueOrDefault(key) + 1;
        var mine = routes[index];
        int pick = -1;
        for (int s = 0; s + 1 < mine.Count; s++)
            if (shared[Key(mine[s], mine[s + 1])] == 1) { pick = s; break; }
        if (pick < 0) pick = (mine.Count - 2) / 2;
        var a = mine[pick];
        var b = mine[pick + 1];
        var dir = (b - a).Normalized();
        return (a + b) / 2 + new Vector2(-dir.Y, dir.X) * BadgeOffset;
    }

    private static string ColorName(Color color)
    {
        for (int i = 0; i < RouteColors.Length; i++)
            if (RouteColors[i].IsEqualApprox(color)) return new[] { "blue", "green", "purple" }[i];
        return color.IsEqualApprox(MarkColor) ? "amber" : "#" + color.ToHtml(false);
    }

    /// <summary>Removes the agent's ink (and a plain route of the fallback).</summary>
    public static void Clear(NMapScreen map)
    {
        map.Drawings?.GetNodeOrNull(LayerName)?.QueueFree();
        map.Drawings?.GetNodeOrNull(BadgesName)?.QueueFree();
        if (Ui.GetField(map, "_mapContainer") is Control container)
            container.GetNodeOrNull(GameViews.RouteNodeName)?.QueueFree();
    }

    // ---- the game's quill ----

    private static bool TryStartPen(NMapDrawings drawings, List<(Color Color, List<Vector2> Points)> strokes, int generation, Action done)
    {
        try
        {
            if (Ui.GetField(drawings, "_lineDrawScene") is not PackedScene lineScene) return false;
            string? layerPath = StaticOrInstanceString(drawings, "_playerDrawingPath");
            if (layerPath == null || ResourceLoader.Load<PackedScene>(layerPath) is not { } layerScene) return false;
            var layer = layerScene.Instantiate();
            layer.Name = LayerName;
            drawings.AddChild(layer);
            if (layer is CanvasItem item) item.Visible = true;
            var viewport = Ui.FindFirst<SubViewport>(layer);
            if (viewport == null)
            {
                layer.QueueFree();
                return false;
            }
            foreach (var canvas in Ui.FindAll<CanvasItem>(layer)) canvas.Visible = true;

            // One stroke after another, a few points per frame, like a hand drawing them.
            int stroke = 0, index = 0;
            Line2D? line = null;
            _ = MainThread.Poll(() =>
            {
                if (generation != _generation || !Ui.IsLive(viewport)) return "replaced";
                for (int n = 0; n < PointsPerFrame; n++)
                {
                    if (stroke >= strokes.Count)
                    {
                        done();
                        return "done";
                    }
                    if (line == null)
                    {
                        line = lineScene.Instantiate<Line2D>();
                        line.DefaultColor = strokes[stroke].Color;
                        line.ClearPoints();
                        line.Position = Vector2.Zero;
                        viewport.AddChild(line);
                    }
                    line.AddPoint(strokes[stroke].Points[index] * ViewportScale);
                    if (++index >= strokes[stroke].Points.Count)
                    {
                        stroke++;
                        index = 0;
                        line = null;
                    }
                }
                return null;
            }, TimeSpan.FromSeconds(30), () => "timeout");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"map ink: the game's quill is unavailable ({ex.Message}); drawing plain lines");
            return false;
        }
    }

    private static string? StaticOrInstanceString(object source, string name)
    {
        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null) return field.GetValue(field.IsStatic ? null : source) as string;
        }
        return null;
    }

    /// <summary>Fallback without the game's scenes: plain lines in the map container (scrolls with the map).</summary>
    private static void DrawPlain(NMapScreen map, List<(Color Color, List<Vector2> Points)> strokes)
    {
        if (Ui.GetField(map, "_mapContainer") is not Control container || map.Drawings is not { } drawings) return;
        var root = new Node2D { Name = GameViews.RouteNodeName, ZIndex = 5 };
        // Points are in the drawings' local space; map them into the container's.
        var toContainer = container.GetGlobalTransform().AffineInverse() * drawings.GetGlobalTransform();
        foreach (var (color, points) in strokes)
        {
            var line = new Line2D { Width = 8, DefaultColor = color, JointMode = Line2D.LineJointMode.Round,
                BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round };
            foreach (var p in points) line.AddPoint(toContainer * p);
            root.AddChild(line);
        }
        container.AddChild(root);
    }

    // ---- hand-drawn shapes (map units, in the drawings' local space) ----

    /// <summary>Same nodes, same wobble: drawing a route again does not make it jump.</summary>
    private static int Seed(string a, string b, int salt)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in a + "|" + b) h = h * 31 + c;
            return h * 31 + salt;
        }
    }

    /// <summary>
    /// A stroke from a toward b, stopping short of both nodes: a slight bow to one side, a slow wave, and a little
    /// jitter, all fading out at the ends so the stroke starts and ends where it aims.
    /// </summary>
    private static List<Vector2> Segment(Vector2 a, Vector2 b, int seed)
    {
        var rng = new Random(seed);
        var dir = b - a;
        float length = dir.Length();
        var result = new List<Vector2>();
        if (length < 1) return result;
        var unit = dir / length;
        float gap = Math.Min(NodeGap, length * 0.3f);
        var start = a + unit * gap;
        var end = b - unit * gap * (0.8f + 0.4f * (float)rng.NextDouble());
        var span = end - start;
        float spanLength = span.Length();
        var normal = new Vector2(-unit.Y, unit.X);

        float bow = spanLength * (0.03f + 0.04f * (float)rng.NextDouble()) * (rng.Next(2) == 0 ? -1 : 1);
        float waveAmp = 1.2f + 1.6f * (float)rng.NextDouble();
        float waveFreq = 1.5f + 1.5f * (float)rng.NextDouble();
        float phase = (float)(rng.NextDouble() * Math.PI * 2);
        int count = Math.Max(3, (int)(spanLength / Step));
        for (int i = 0; i <= count; i++)
        {
            float t = i / (float)count;
            float envelope = MathF.Sin(MathF.PI * t);
            float offset = bow * envelope
                           + waveAmp * envelope * MathF.Sin(MathF.PI * 2 * waveFreq * t + phase)
                           + 0.5f * envelope * (float)(rng.NextDouble() * 2 - 1);
            result.Add(start + span * t + normal * offset);
        }
        return result;
    }

    /// <summary>A loop around a node, a bit wider than tall, overshooting its start like a quick hand-drawn circle.</summary>
    private static List<Vector2> Circle(Vector2 center, int seed)
    {
        var rng = new Random(seed);
        float start = (float)(rng.NextDouble() * Math.PI * 2);
        float sweep = MathF.PI * 2 * 1.12f;
        float rx = MarkRadius * (1.05f + 0.1f * (float)rng.NextDouble());
        float ry = MarkRadius * (0.9f + 0.1f * (float)rng.NextDouble());
        float tilt = (float)(rng.NextDouble() - 0.5) * 0.4f;
        float phase = (float)(rng.NextDouble() * Math.PI * 2);
        var result = new List<Vector2>();
        int count = (int)(sweep * MarkRadius / Step);
        for (int i = 0; i <= count; i++)
        {
            float t = i / (float)count;
            float angle = start + sweep * t;
            // The radius drifts slowly and the loop spirals out a little, so the ends do not meet exactly.
            float drift = 1f + 0.05f * MathF.Sin(angle * 2 + phase) + 0.06f * t;
            var p = new Vector2(MathF.Cos(angle) * rx, MathF.Sin(angle) * ry) * drift;
            result.Add(center + p.Rotated(tilt));
        }
        return result;
    }
}
