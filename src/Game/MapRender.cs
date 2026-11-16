using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Draws the whole act map as one tall image with the game's own map icons: lines between nodes, the path taken,
/// the current position, and the next choices labelled with the indices `choose &lt;i&gt;` expects on the map screen.
/// </summary>
internal static class MapRender
{
    private const int SpacingX = 96;
    private const int SpacingY = 74;
    private const int Margin = 64;
    private const int HeaderHeight = 70;
    private const int FooterHeight = 64;
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
        int MaxRow);

    /// <summary>Main thread: snapshot the map model. `next` must be in the same order as the map handler's options.</summary>
    private static MapData Snapshot(RunState run, IReadOnlyList<(int Col, int Row)> next)
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

        return new MapData(
            run.CurrentActIndex + 1,
            boss,
            nodes,
            run.VisitedMapCoords.Select(c => (c.col, c.row)).ToList(),
            next.ToList(),
            ancient,
            Math.Max(nodes.Max(n => n.Col) + 1, SafeInt(map.GetColumnCount)),
            nodes.Max(n => n.Row));
    }

    private static int SafeInt(Func<int> get)
    {
        try { return get(); } catch { return 0; }
    }

    public static async Task<CaptureResult> Render(RunState run, IReadOnlyList<(int Col, int Row)> next, int maxWidth)
    {
        var viewport = await MainThread.Run(() => Build(Snapshot(run, next)));
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
        int height = HeaderHeight + Margin * 2 + data.MaxRow * SpacingY + FooterHeight;
        var viewport = new SubViewport
        {
            Size = new Vector2I(width, height),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        Ui.Root.AddChild(viewport);
        viewport.AddChild(new ColorRect { Color = Parchment, Size = viewport.Size });

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
            int nextIndex = data.Next.IndexOf(key);
            if (nextIndex >= 0)
            {
                AddRing(viewport, center, IconSize * 0.66f, NextColor, 5);
                AddLabel(viewport, nextIndex.ToString(), center + new Vector2(IconSize * 0.45f, -IconSize * 0.95f),
                    new Vector2(40, 40), 30, Colors.White, NextColor);
            }
        }

        string header = $"Act {data.Act}" + (data.Boss != null ? $" · BOSS: {data.Boss}" : "");
        AddLabel(viewport, header, new Vector2(0, 14), new Vector2(width, 44), 30, Ink, Colors.Transparent);
        AddLabel(viewport, "red: path taken · green: next choices (choose <i>)", new Vector2(0, height - FooterHeight + 14),
            new Vector2(width, 36), 20, Ink, Colors.Transparent);
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
