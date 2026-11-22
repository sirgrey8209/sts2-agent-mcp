using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Draws the whole act map as one tall image with the game's own map icons: lines between nodes, the path taken,
/// the current position, and the next choices labelled with the indices `choose &lt;i&gt;` expects on the map screen.
/// Optional annotations from the caller: node coordinates, extra paths, marked nodes with text, and notes.
/// </summary>
internal static class MapRender
{
    /// <summary>A path through map nodes, given as references ("r7c3", "current", "boss").</summary>
    public sealed record PathSpec(IReadOnlyList<string> Nodes, string? Color, string? Label);

    /// <summary>A highlighted node with optional text next to it.</summary>
    public sealed record MarkSpec(string Node, string? Color, string? Text);

    public sealed record Annotations(bool Coords, IReadOnlyList<PathSpec> Paths, IReadOnlyList<MarkSpec> Marks, IReadOnlyList<string> Notes)
    {
        public static readonly Annotations None = new(false, Array.Empty<PathSpec>(), Array.Empty<MarkSpec>(), Array.Empty<string>());
    }

    private sealed record ResolvedPath(List<(int Col, int Row)> Nodes, Color Color, string? Label);
    private sealed record ResolvedMark((int Col, int Row) Node, Color Color, string? Text);

    private const int SpacingX = 96;
    private const int SpacingY = 74;
    private const int Margin = 64;
    private const int HeaderHeight = 70;
    private const int FooterHeight = 64;
    private const int LegendLine = 32;
    private const int NoteFont = 20;
    private const int IconSize = 52;

    private static readonly Color Parchment = new(0.86f, 0.80f, 0.66f);
    private static readonly Color Ink = new(0.25f, 0.18f, 0.12f);
    private static readonly Color PathColor = new(0.70f, 0.12f, 0.10f);
    private static readonly Color NextColor = new(0.05f, 0.45f, 0.20f);

    private sealed record MapNode(int Col, int Row, MapPointType Type, List<(int Col, int Row)> Children);

    private sealed record MapData(
        int Act,
        string? Boss,
        List<MapNode> Nodes,
        List<(int Col, int Row)> Visited,
        List<(int Col, int Row)> Next,
        Texture2D? AncientIcon,
        int Columns,
        int MaxRow,
        bool Coords,
        List<ResolvedPath> Paths,
        List<ResolvedMark> Marks,
        List<string> Notes);

    // Default colors for caller paths and marks, in order (distinct from the red taken path and green next choices).
    private static readonly Color[] Palette =
    {
        new(0.12f, 0.35f, 0.80f), new(0.55f, 0.20f, 0.70f), new(0.90f, 0.50f, 0.05f),
        new(0.00f, 0.55f, 0.60f), new(0.85f, 0.20f, 0.55f), new(0.45f, 0.30f, 0.15f),
    };

    /// <summary>Main thread: snapshot the map model. `next` must be in the same order as the map handler's options.</summary>
    private static MapData Snapshot(RunState run, IReadOnlyList<(int Col, int Row)> next, Annotations notes)
    {
        var map = run.Map;
        var points = map.GetAllMapPoints().ToList();
        points.Add(map.StartingMapPoint);
        try { points.Add(map.BossMapPoint); } catch { /* not generated */ }
        try { if (map.SecondBossMapPoint != null) points.Add(map.SecondBossMapPoint); } catch { }

        var nodes = points
            .GroupBy(p => (p.coord.col, p.coord.row))
            .Select(g => g.First())
            .Select(p => new MapNode(p.coord.col, p.coord.row, p.PointType,
                p.Children.Select(c => (c.coord.col, c.coord.row)).ToList()))
            .ToList();

        string? boss = null;
        try { boss = Text.Of(() => run.Act.BossEncounter.Title); } catch { }
        Texture2D? ancient = null;
        try { ancient = run.Act.Ancient?.MapIcon; } catch { }

        var visited = run.VisitedMapCoords.Select(c => (c.col, c.row)).ToList();
        var known = nodes.Select(n => (n.Col, n.Row)).ToHashSet();
        (int, int)? bossCoord = nodes.Where(n => n.Type == MapPointType.Boss).Select(n => ((int, int)?)(n.Col, n.Row)).FirstOrDefault();
        (int Col, int Row) Resolve(string reference) => ResolveNode(reference, known, visited, bossCoord);

        int colorIndex = 0;
        Color Pick(string? name) => ParseColor(name) ?? Palette[colorIndex++ % Palette.Length];
        var paths = notes.Paths.Select(p =>
        {
            var list = p.Nodes.Select(Resolve).ToList();
            if (list.Count < 2) throw new ToolError("a path needs at least two nodes");
            return new ResolvedPath(list, Pick(p.Color), p.Label);
        }).ToList();
        var marks = notes.Marks.Select(m => new ResolvedMark(Resolve(m.Node), Pick(m.Color), m.Text)).ToList();

        return new MapData(
            run.CurrentActIndex + 1,
            boss,
            nodes,
            visited,
            next.ToList(),
            ancient,
            Math.Max(nodes.Max(n => n.Col) + 1, SafeInt(map.GetColumnCount)),
            nodes.Max(n => n.Row),
            notes.Coords,
            paths,
            marks,
            notes.Notes.Where(n => !string.IsNullOrWhiteSpace(n)).ToList());
    }

    /// <summary>"r7c3" / "r7 c3" / "c3r7", "current", "boss" -> map coordinate.</summary>
    private static (int Col, int Row) ResolveNode(string reference, HashSet<(int, int)> known,
        List<(int Col, int Row)> visited, (int, int)? boss)
    {
        string r = reference.Trim().ToLowerInvariant().Replace(" ", "");
        if (r == "current")
            return visited.Count > 0 ? visited[^1] : throw new ToolError("\"current\": not on the map yet");
        if (r == "boss")
            return boss ?? throw new ToolError("\"boss\": no boss node on this map");
        var m = Regex.Match(r, @"^(?:r(?<row>\d+)c(?<col>\d+)|c(?<col>\d+)r(?<row>\d+))$");
        if (!m.Success) throw new ToolError($"bad node \"{reference}\" (use r<row>c<col> like r7c3, \"current\" or \"boss\")");
        var coord = (int.Parse(m.Groups["col"].Value), int.Parse(m.Groups["row"].Value));
        if (!known.Contains(coord)) throw new ToolError($"no map node at {reference} (render with coords=true to see them)");
        return coord;
    }

    private static Color? ParseColor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sentinel = new Color(0, 0, 0, 0);
        var color = Color.FromString(name.Trim(), sentinel);
        return color == sentinel ? throw new ToolError($"unknown color \"{name}\" (a name like blue or #3366cc)") : color;
    }

    private static int SafeInt(Func<int> get)
    {
        try { return get(); } catch { return 0; }
    }

    public static async Task<CaptureResult> Render(RunState run, IReadOnlyList<(int Col, int Row)> next, int maxWidth,
        Annotations? notes = null)
    {
        var viewport = await MainThread.Run(() => Build(Snapshot(run, next, notes ?? Annotations.None)));
        try
        {
            int frames = 0;
            await MainThread.Poll(() => ++frames >= 3 ? "ok" : null, TimeSpan.FromSeconds(5), () => "timeout");
            return await MainThread.Run(() => Capture.SaveViewport(viewport, "map", maxWidth));
        }
        finally
        {
            await MainThread.Run(viewport.QueueFree);
        }
    }

    private static SubViewport Build(MapData data)
    {
        int width = Margin * 2 + (data.Columns - 1) * SpacingX;
        int mapBottom = HeaderHeight + Margin * 2 + data.MaxRow * SpacingY;
        int textWidth = width - Margin;
        var labelled = data.Paths.Where(p => !string.IsNullOrWhiteSpace(p.Label)).ToList();
        // Notes are measured after layout; the image grows to fit them at the end.
        int height = mapBottom + FooterHeight + labelled.Count * LegendLine;
        var viewport = new SubViewport
        {
            Size = new Vector2I(width, height),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        Ui.Root.AddChild(viewport);
        var background = new ColorRect { Color = Parchment, Size = viewport.Size };
        viewport.AddChild(background);

        Vector2 Pos((int Col, int Row) c) =>
            new(Margin + c.Col * SpacingX, HeaderHeight + Margin + (data.MaxRow - c.Row) * SpacingY);

        var visited = data.Visited.ToHashSet();
        var visitedEdges = new HashSet<((int, int), (int, int))>();
        for (int i = 1; i < data.Visited.Count; i++) visitedEdges.Add((data.Visited[i - 1], data.Visited[i]));
        var current = data.Visited.Count > 0 ? data.Visited[^1] : ((int, int)?)null;

        // Edges first, icons on top.
        foreach (var node in data.Nodes)
        {
            foreach (var child in node.Children)
            {
                bool taken = visitedEdges.Contains(((node.Col, node.Row), child));
                bool isNext = current == (node.Col, node.Row) && data.Next.Contains(child);
                var color = taken ? PathColor : isNext ? NextColor : new Color(Ink, 0.45f);
                AddLine(viewport, Pos((node.Col, node.Row)), Pos(child), color, taken || isNext ? 5 : 2.5f);
            }
        }

        // Caller paths over the map lines, under the icons. Paths sharing a segment are drawn side by side.
        for (int i = 0; i < data.Paths.Count; i++)
        {
            float offset = (i - (data.Paths.Count - 1) / 2f) * 7f;
            var path = data.Paths[i];
            var points = new List<Vector2>();
            for (int j = 0; j < path.Nodes.Count; j++)
            {
                var here = Pos(path.Nodes[j]);
                var along = j + 1 < path.Nodes.Count ? Pos(path.Nodes[j + 1]) - here : here - Pos(path.Nodes[j - 1]);
                var normal = new Vector2(-along.Y, along.X).Normalized();
                points.Add(here + normal * offset);
            }
            viewport.AddChild(new Line2D
            {
                Points = points.ToArray(), DefaultColor = new Color(path.Color, 0.85f), Width = 7, Antialiased = true,
                JointMode = Line2D.LineJointMode.Round, BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round,
            });
        }

        foreach (var node in data.Nodes)
        {
            var center = Pos((node.Col, node.Row));
            var key = (node.Col, node.Row);
            bool past = node.Row <= (current?.Item2 ?? -1) && !visited.Contains(key);

            if (node.Type == MapPointType.Boss)
            {
                AddLabel(viewport, "BOSS", center - new Vector2(80, 26), new Vector2(160, 52), 34, Ink, Colors.Transparent);
                continue;
            }

            bool ancient = node.Type == MapPointType.Ancient && data.AncientIcon != null;
            var texture = ancient ? data.AncientIcon : Icon(node.Type);
            // The ancient icon is drawn light for the game's dark map backdrop; give it a dark disc on parchment.
            if (ancient) AddDisc(viewport, center, IconSize * 0.62f, Ink);
            if (texture != null)
            {
                viewport.AddChild(new TextureRect
                {
                    Texture = texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(IconSize, IconSize),
                    Position = center - new Vector2(IconSize / 2f, IconSize / 2f),
                    Modulate = past ? new Color(1, 1, 1, 0.3f) : Colors.White,
                });
            }
            else
            {
                AddLabel(viewport, node.Type.ToString()[..1], center - new Vector2(20, 20), new Vector2(40, 40), 26, Ink, Colors.Transparent);
            }

            if (visited.Contains(key)) AddRing(viewport, center, IconSize * 0.62f, PathColor, key == current ? 6 : 3);
            if (data.Coords && node.Type != MapPointType.Boss)
                AddText(viewport, $"r{node.Row}c{node.Col}", center + new Vector2(-24, IconSize * 0.40f), 15, new Color(Ink, 0.9f));
            int nextIndex = data.Next.IndexOf(key);
            if (nextIndex >= 0)
            {
                AddRing(viewport, center, IconSize * 0.66f, NextColor, 5);
                AddLabel(viewport, nextIndex.ToString(), center + new Vector2(IconSize * 0.45f, -IconSize * 0.95f),
                    new Vector2(40, 40), 30, Colors.White, NextColor);
            }
        }

        // Marked nodes: a ring in the mark color and the text beside the node (left of it near the right edge).
        foreach (var mark in data.Marks)
        {
            var center = Pos(mark.Node);
            AddRing(viewport, center, IconSize * 0.78f, mark.Color, 5);
            if (string.IsNullOrWhiteSpace(mark.Text)) continue;
            float guess = TextEm(mark.Text) * 22;
            bool left = center.X + IconSize * 0.8f + guess > width;
            var at = left ? center + new Vector2(-IconSize * 0.8f - guess, -16) : center + new Vector2(IconSize * 0.8f, -16);
            AddText(viewport, mark.Text, at, 22, mark.Color);
        }

        string header = $"Act {data.Act}" + (data.Boss != null ? $" · BOSS: {data.Boss}" : "");
        AddLabel(viewport, header, new Vector2(0, 14), new Vector2(width, 44), 30, Ink, Colors.Transparent);
        AddLabel(viewport, "red: path taken · green: next choices (choose <i>)", new Vector2(0, mapBottom + 14),
            new Vector2(width, 36), 20, Ink, Colors.Transparent);

        float y = mapBottom + FooterHeight;
        foreach (var path in labelled)
        {
            float x = Margin / 2f;
            viewport.AddChild(new Line2D
            {
                Points = new[] { new Vector2(x, y + 14), new Vector2(x + 44, y + 14) },
                DefaultColor = new Color(path.Color, 0.85f), Width = 7, Antialiased = true,
            });
            AddText(viewport, path.Label!, new Vector2(x + 56, y), 20, Ink);
            y += LegendLine;
        }
        if (data.Notes.Count > 0) y += 12;
        foreach (var note in data.Notes)
        {
            var label = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                LabelSettings = new LabelSettings { FontSize = NoteFont, FontColor = Ink, LineSpacing = 6 },
                Text = note,
            };
            viewport.AddChild(label);
            label.Position = new Vector2(Margin / 2f, y);
            label.Size = new Vector2(textWidth, 0);
            // Wrapped height for this width: lines x line height, with an estimate as a fallback.
            float measured = 0;
            try { measured = label.GetLineCount() * (label.GetLineHeight() + 6); } catch { /* estimate below */ }
            float lineHeight = measured > 0 ? measured : NoteLines(note, textWidth) * (NoteFont * 1.45f + 6);
            label.Size = new Vector2(textWidth, lineHeight);
            y += lineHeight + 4;
        }

        int finalHeight = Math.Max(height, (int)Math.Ceiling(y + 16));
        if (finalHeight != height)
        {
            viewport.Size = new Vector2I(width, finalHeight);
            background.Size = viewport.Size;
        }
        return viewport;
    }

    private static readonly Dictionary<MapPointType, Texture2D?> IconCache = new();

    private static Texture2D? Icon(MapPointType type)
    {
        if (IconCache.TryGetValue(type, out var cached)) return cached;
        string? name = type switch
        {
            MapPointType.Monster => "map_monster",
            MapPointType.Elite => "map_elite",
            MapPointType.Treasure => "map_chest",
            MapPointType.Shop => "map_shop",
            MapPointType.RestSite => "map_rest",
            MapPointType.Unknown or MapPointType.Ancient or MapPointType.Unassigned => "map_unknown",
            _ => null,
        };
        Texture2D? texture = null;
        if (name != null)
        {
            try
            {
                texture = ResourceLoader.Load<Texture2D>($"res://images/atlases/ui_atlas.sprites/map/icons/{name}.tres",
                    null, ResourceLoader.CacheMode.Reuse);
            }
            catch (Exception ex)
            {
                Log.Warn($"map icon {name} unavailable: {ex.Message}");
            }
        }
        IconCache[type] = texture;
        return texture;
    }

    private static void AddLine(Node parent, Vector2 from, Vector2 to, Color color, float width)
    {
        // Stop short of the icons so lines don't cross them.
        var direction = (to - from).Normalized();
        float inset = IconSize * 0.5f;
        parent.AddChild(new Line2D
        {
            Points = new[] { from + direction * inset, to - direction * inset },
            DefaultColor = color,
            Width = width,
            Antialiased = true,
        });
    }

    private static void AddRing(Node parent, Vector2 center, float radius, Color color, float width)
    {
        const int segments = 40;
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        parent.AddChild(new Line2D { Points = points, DefaultColor = color, Width = width, Antialiased = true });
    }

    private static void AddDisc(Node parent, Vector2 center, float radius, Color color)
    {
        const int segments = 40;
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        parent.AddChild(new Polygon2D { Polygon = points, Color = color, Antialiased = true });
    }

    /// <summary>Approximate text width in ems (CJK glyphs about one em, Latin about half).</summary>
    private static float TextEm(string text)
    {
        float em = 0;
        foreach (char c in text) em += c > 0x2E80 ? 1f : 0.55f;
        return em;
    }

    /// <summary>Rough wrapped line count for a note.</summary>
    private static int NoteLines(string text, int width) =>
        Math.Max(1, (int)Math.Ceiling(TextEm(text) * NoteFont / (width * 0.92f)));

    /// <summary>Free text with a parchment outline so it stays legible over lines and icons.</summary>
    private static void AddText(Node parent, string text, Vector2 position, int fontSize, Color color)
    {
        parent.AddChild(new Label
        {
            Text = text,
            Position = position,
            LabelSettings = new LabelSettings { FontSize = fontSize, FontColor = color, OutlineSize = 6, OutlineColor = Parchment },
        });
    }

    private static void AddLabel(Node parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, Color background)
    {
        if (background.A > 0)
            parent.AddChild(new ColorRect { Color = background, Position = position, Size = size });
        parent.AddChild(new Label
        {
            Text = text,
            Position = position,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings { FontSize = fontSize, FontColor = color },
        });
    }
}
