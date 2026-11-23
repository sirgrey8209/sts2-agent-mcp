using System;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Sts2AgentMcp.Screens;

namespace Sts2AgentMcp.Game;

/// <summary>
/// What the chat's marks point at, by the ids <c>look</c> shows: a hand card (<c>c3</c>), a creature (<c>e1</c>,
/// <c>me</c>, <c>pet1</c>), a potion slot (<c>p0</c>), a relic (<c>relic:BURNING_BLOOD</c> or its name), an option of
/// the current screen (<c>opt2</c>: the number <c>choose</c> takes), the top bar's deck and map buttons
/// (<c>deck_button</c>, <c>map_button</c>), or a map node (<c>r7c3</c>, <c>current</c>, <c>boss</c>; drawn by
/// <see cref="MapInk"/>). A locator gives the target's rect in the game's canvas each time it is asked (cards move,
/// creatures die), or null while it is not on screen. Main thread only.
/// </summary>
internal static class ScreenTargets
{
    private static readonly Regex MapRef = new(@"^(?:r\d+c\d+|c\d+r\d+|current|boss)$", RegexOptions.IgnoreCase);

    /// <summary>A map node reference (drawn on the map by <see cref="MapInk"/>, not by a locator).</summary>
    public static bool IsMapNode(string target) => MapRef.IsMatch(target.Trim()) && !IsHandCard(target);

    // "c3" is a hand card; map nodes are r<row>c<col>.
    private static bool IsHandCard(string target) => Regex.IsMatch(target.Trim(), @"^c\d+$");

    /// <summary>A locator for the target, or null with the reason.</summary>
    public static Func<Rect2?>? Locate(string target, out string? error)
    {
        error = null;
        string t = target.Trim();
        string lower = t.ToLowerInvariant();

        if (IsHandCard(lower))
        {
            var card = Ids.ResolveCard(lower);
            if (card == null) { error = $"no card {t} in this combat"; return null; }
            return () =>
            {
                var holder = NPlayerHand.Instance?.GetCardHolder(card);
                return holder != null && Ui.IsVisible(holder) ? RectOf(holder.Hitbox ?? (Control)holder) : null;
            };
        }

        if (lower == "me" || lower.StartsWith("e") && lower.Length > 1 && char.IsDigit(lower[1]) || lower.StartsWith("pet"))
        {
            var ctx = GameCtx.Capture();
            var creature = Ids.ResolveCreature(ctx.Combat, ctx.Me?.Creature, lower);
            if (creature == null) { error = $"no creature {t} in this combat"; return null; }
            return () =>
            {
                var node = NCombatRoom.Instance?.GetCreatureNode(creature);
                return node != null && Ui.IsLive(node) && node.Hitbox is { } hitbox ? RectOf(hitbox) : null;
            };
        }

        if (Regex.Match(lower, @"^p(\d+)$") is { Success: true } potion)
        {
            int slot = int.Parse(potion.Groups[1].Value);
            return () =>
            {
                var holders = PotionHolders();
                return slot < holders.Length ? RectOf(holders[slot]) : null;
            };
        }

        if (Regex.Match(lower, @"^opt(\d+)$") is { Success: true } option)
        {
            int index = int.Parse(option.Groups[1].Value);
            if (ScreenRegistry.Target(GameCtx.Capture(), index) == null) { error = $"no option {index} on this screen"; return null; }
            return () => ScreenRegistry.Target(GameCtx.Capture(), index) is { } c && Ui.IsVisible(c) ? RectOf(c) : null;
        }

        if (lower is "deck_button" or "map_button")
        {
            return () =>
            {
                var bar = Ui.FindFirst<NTopBar>(Ui.Root);
                Control? button = bar == null ? null : lower == "deck_button" ? bar.Deck : bar.Map;
                return button != null && Ui.IsVisible(button) ? RectOf(button) : null;
            };
        }

        if (lower.StartsWith("relic:"))
        {
            string query = t[6..].Trim();
            return () =>
            {
                var holder = Ui.FindAll<NRelicInventoryHolder>(Ui.Root).FirstOrDefault(h =>
                    h.Relic?.Model is { } m &&
                    (m.Id.Entry.Equals(query, StringComparison.OrdinalIgnoreCase) || Text.Of(() => m.Title) == query));
                return holder != null && Ui.IsVisible(holder) ? RectOf(holder) : null;
            };
        }

        error = $"unknown target '{t}' (c<n>, e<n>, me, p<n>, opt<n>, relic:<id>, deck_button, map_button, r<row>c<col>)";
        return null;
    }

    /// <summary>A short name of the target for the player (the card's or creature's game name), or the id.</summary>
    public static string DisplayName(string target)
    {
        string lower = target.Trim().ToLowerInvariant();
        if (IsHandCard(lower) && Ids.ResolveCard(lower) is { } card) return View.Fmt.CardName(card);
        var ctx = GameCtx.Capture();
        if (Ids.ResolveCreature(ctx.Combat, ctx.Me?.Creature, lower) is { } creature)
            return Text.Of(() => creature.Name) ?? target;
        return target;
    }

    private static NPotionHolder[] PotionHolders()
    {
        var bar = Ui.FindFirst<NTopBar>(Ui.Root);
        return bar?.PotionContainer is Node container ? Ui.FindAllByPosition<NPotionHolder>(container).ToArray() : Array.Empty<NPotionHolder>();
    }

    private static Rect2? RectOf(Control control) => Ui.IsLive(control) ? control.GetGlobalRect() : null;
}
