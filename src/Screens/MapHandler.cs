using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>
/// Map: travelable next nodes plus the remaining map as compact rows.
/// Row format: "r7: 0M>0,1  2E>1  4R>4" = column, type letter, child columns on the next row.
/// </summary>
internal sealed class MapHandler : IScreenHandler
{
    public string Kind => "map";

    public bool Matches(GameCtx ctx) => ctx.Screen is NMapScreen && ctx.Run != null;

    private static List<NMapPoint> Travelable(NMapScreen screen) =>
        Ui.FindAll<NMapPoint>(screen)
            .Where(p => p.State == MapPointState.Travelable && p.Point != null)
            .OrderBy(p => p.Point!.coord.col)
            .ToList();

    /// <summary>
    /// Coordinates of the next choices, in the order `choose &lt;i&gt;` uses. Falls back to the current node's children
    /// when the map screen has no travelable points (e.g. it is closed during a fight).
    /// </summary>
    public static List<(int Col, int Row)> NextCoords(RunState run)
    {
        var screen = NMapScreen.Instance;
        var fromScreen = screen != null ? Travelable(screen).Select(p => (p.Point!.coord.col, p.Point!.coord.row)).ToList() : new();
        if (fromScreen.Count > 0) return fromScreen;
        if (run.VisitedMapCoords.Count == 0) return new();
        var current = run.Map.GetPoint(run.VisitedMapCoords[^1]);
        return current?.Children.OrderBy(c => c.coord.col).Select(c => (c.coord.col, c.coord.row)).ToList() ?? new();
    }

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var run = ctx.Run!;
        var map = run.Map;
        var screen = (NMapScreen)ctx.Screen!;

        int currentRow = -1;
        if (run.VisitedMapCoords.Count > 0)
        {
            var cur = run.VisitedMapCoords[^1];
            currentRow = cur.row;
            view.Add($"at r{cur.row} c{cur.col} {map.GetPoint(cur)?.PointType}");
        }

        try
        {
            var boss = run.Act.BossEncounter;
            view.Add($"boss: {Text.Of(() => boss.Title) ?? boss.Id.Entry}");
        }
        catch { /* act not fully set up yet */ }

        var options = Travelable(screen);
        view.Add("next:");
        for (int i = 0; i < options.Count; i++)
        {
            var p = options[i].Point!;
            view.Add($"  {i} r{p.coord.row} c{p.coord.col} {p.PointType}");
        }

        view.Add("legend: M monster, E elite, R rest, S shop, T treasure, ? unknown, A ancient, B boss");
        var rows = map.GetAllMapPoints()
            .Where(p => full || p.coord.row > currentRow)
            .GroupBy(p => p.coord.row)
            .OrderBy(g => g.Key);
        foreach (var row in rows)
        {
            var nodes = row.OrderBy(p => p.coord.col)
                .Select(p => $"{p.coord.col}{Letter(p.PointType)}" +
                             (p.Children.Count > 0 ? ">" + string.Join(",", p.Children.Select(c => c.coord.col).OrderBy(c => c)) : ""));
            view.Add($"  r{row.Key}: " + string.Join("  ", nodes));
        }
        try { view.Add($"  r{map.BossMapPoint.coord.row}: B"); } catch { }

        view.Actions = "choose <i>";
        return view;
    }

    private static string Letter(MapPointType type) => type switch
    {
        MapPointType.Monster => "M",
        MapPointType.Elite => "E",
        MapPointType.RestSite => "R",
        MapPointType.Shop => "S",
        MapPointType.Treasure => "T",
        MapPointType.Unknown => "?",
        MapPointType.Ancient => "A",
        MapPointType.Boss => "B",
        _ => type.ToString(),
    };

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        if (action.Verb != "choose") return ExecResult.Fail($"unknown map action '{action.Verb}'");
        var screen = (NMapScreen)ctx.Screen!;
        var options = Travelable(screen);
        int? i = action.IntArg(0);
        if (i == null || i < 0 || i >= options.Count) return ExecResult.Fail($"choose 0-{options.Count - 1}");
        screen.OnMapPointSelectedLocally(options[i.Value]);
        return ExecResult.Ok(SettleMode.ScreenChange);
    }
}
