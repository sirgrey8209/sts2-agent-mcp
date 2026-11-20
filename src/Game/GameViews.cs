using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Opens the game's own views for the chat agent (docs/chat-roadmap.md step 7): the deck, a combat pile, the map,
/// a card's or relic's detail view, and a route drawn on the real map. Only views: nothing here plays or chooses.
/// Main thread only.
/// </summary>
internal static class GameViews
{
    public const string Help = "deck, draw, discard, exhaust, map, card:<id or name>, relic:<id or name>";

    private const string RouteNodeName = "AgentRoute";

    public static string Open(string view)
    {
        var ctx = GameCtx.Capture();
        var me = ctx.Me ?? throw new ToolError("no run in progress");
        string v = view.Trim();
        switch (v.ToLowerInvariant())
        {
            case "deck":
                CloseCapstone();
                NDeckViewScreen.ShowScreen(me);
                return "opened the deck";
            case "draw" or "discard" or "exhaust":
            {
                var pcs = me.PlayerCombatState;
                if (!ctx.InCombat || pcs == null) throw new ToolError("not in combat");
                var pile = v == "draw" ? pcs.DrawPile : v == "discard" ? pcs.DiscardPile : pcs.ExhaustPile;
                CloseCapstone();
                NCardPileScreen.ShowScreen(pile, Array.Empty<string>());
                return $"opened the {v} pile ({pile.Cards.Count} cards)";
            }
            case "map":
                OpenMap();
                return "opened the map";
        }
        if (v.StartsWith("card:", StringComparison.OrdinalIgnoreCase))
        {
            string query = v[5..].Trim();
            var card = Ids.ResolveCard(query) ?? Catalog.FindCard(query) ?? throw new ToolError($"card '{query}' not found");
            var cards = new List<CardModel> { card.IsMutable ? card : card.ToMutable() };
            NGame.Instance!.GetInspectCardScreen().Open(cards, 0);
            return "opened the card: " + View.Fmt.CardName(card);
        }
        if (v.StartsWith("relic:", StringComparison.OrdinalIgnoreCase))
        {
            string query = v[6..].Trim();
            var relic = FindRelic(me.Relics, query) ?? Catalog.FindRelic(query) ?? throw new ToolError($"relic '{query}' not found");
            var list = me.Relics.Contains(relic) ? me.Relics.ToList() : new List<RelicModel> { relic };
            NGame.Instance!.GetInspectRelicScreen().Open(list, relic);
            return "opened the relic: " + Text.Of(() => relic.Title);
        }
        throw new ToolError($"unknown view '{view}' (use {Help})");
    }

    /// <summary>The player's relic matching an id or a displayed name.</summary>
    public static RelicModel? FindRelic(IEnumerable<RelicModel> relics, string query) =>
        relics.FirstOrDefault(r =>
            r.Id.Entry.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            r.Id.ToString().Equals(query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Text.Of(() => r.Title), query, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opens the map and draws a route over it through the given nodes ("r&lt;row&gt;c&lt;col&gt;", "current", "boss"),
    /// replacing any route drawn before. An empty list only clears it.
    /// </summary>
    public static string DrawRoute(IReadOnlyList<string> nodes, Color color)
    {
        if (GameCtx.Capture().Run == null) throw new ToolError("no run in progress");
        var map = OpenMap();
        var container = Ui.GetField(map, "_mapContainer") as Control ?? throw new ToolError("map not ready");
        container.GetNodeOrNull(RouteNodeName)?.QueueFree();
        if (nodes.Count == 0) return "cleared the route";
        if (nodes.Count < 2) throw new ToolError("a route needs at least two nodes");

        var points = MapPoints(map);
        var line = new Line2D
        {
            Name = RouteNodeName,
            Width = 10,
            DefaultColor = color,
            JointMode = Line2D.LineJointMode.Round,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
            ZIndex = 5,
        };
        var inverse = container.GetGlobalTransform().AffineInverse();
        foreach (var reference in nodes)
        {
            var point = Resolve(reference, points);
            line.AddPoint(inverse * (point.GlobalPosition + point.Size * point.GetGlobalTransform().Scale / 2));
        }
        container.AddChild(line);
        return $"drew a route through {nodes.Count} nodes";
    }

    private static NMapScreen OpenMap()
    {
        var map = NMapScreen.Instance ?? throw new ToolError("no map (not in a run)");
        CloseCapstone();
        if (!map.IsOpen) map.Open();
        return map;
    }

    private static void CloseCapstone()
    {
        if (NCapstoneContainer.Instance is { InUse: true } capstone) capstone.Close();
    }

    private static Dictionary<(int Col, int Row), NMapPoint> MapPoints(NMapScreen map)
    {
        var result = new Dictionary<(int, int), NMapPoint>();
        if (Ui.GetField(map, "_mapPointDictionary") is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                if (entry.Key is MapCoord c && entry.Value is NMapPoint p) result[(c.col, c.row)] = p;
        }
        if (result.Count == 0) throw new ToolError("map points not found");
        return result;
    }

    private static NMapPoint Resolve(string reference, Dictionary<(int Col, int Row), NMapPoint> points)
    {
        string r = reference.Trim().ToLowerInvariant();
        if (r == "current")
        {
            var coord = GameCtx.Capture().Run?.CurrentMapCoord ?? throw new ToolError("\"current\": not on the map yet");
            return points.TryGetValue((coord.col, coord.row), out var here) ? here : throw new ToolError("\"current\": node not drawn");
        }
        if (r == "boss")
        {
            return points.Values.FirstOrDefault(p => p.Point.PointType == MapPointType.Boss)
                   ?? points.Values.OrderByDescending(p => p.Point.coord.row).First();
        }
        var m = Regex.Match(r, @"^(?:r(?<row>\d+)c(?<col>\d+)|c(?<col>\d+)r(?<row>\d+))$");
        if (!m.Success) throw new ToolError($"bad node \"{reference}\" (use r<row>c<col> like r7c3, \"current\" or \"boss\")");
        var key = (int.Parse(m.Groups["col"].Value), int.Parse(m.Groups["row"].Value));
        return points.TryGetValue(key, out var point) ? point : throw new ToolError($"no map node at {reference}");
    }
}
