using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.View;

/// <summary>
/// The single place that decides how game objects are written for the agent.
/// Game text is used as-is (tags stripped); structure words are short English tokens.
/// </summary>
internal static class Fmt
{
    // ---------- cards ----------

    public static string Cost(CardModel card)
    {
        string energy;
        try { energy = card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString(); }
        catch { energy = "?"; }
        string? stars = null;
        try
        {
            if (card.HasStarCostX) stars = "X";
            else if (card.CurrentStarCost >= 0) stars = card.GetStarCostWithModifiers().ToString();
        }
        catch { /* no star cost */ }
        return stars == null ? energy + "E" : $"{energy}E {stars}★";
    }

    /// <summary>The game's title already carries the upgrade marker ("타격+").</summary>
    public static string CardName(CardModel card) => Text.Of(() => card.Title) ?? card.Id.Entry;

    /// <summary>Name plus enchantment and affliction, e.g. "타격+ [날카로움 2] {위축 1}".</summary>
    public static string CardLabel(CardModel card)
    {
        string label = CardName(card);
        if (EnchantmentLabel(card) is { } ench) label += $" [{ench}]";
        if (AfflictionLabel(card) is { } aff) label += $" {{{aff}}}";
        return label;
    }

    public static string? EnchantmentLabel(CardModel card)
    {
        try
        {
            var e = card.Enchantment;
            if (e == null) return null;
            string title = Text.Of(() => e.Title) ?? e.Id.Entry;
            return e.ShowAmount ? $"{title} {e.DisplayAmount}" : title;
        }
        catch { return null; }
    }

    public static string? AfflictionLabel(CardModel card)
    {
        try
        {
            var a = card.Affliction;
            if (a == null) return null;
            string title = Text.Of(() => a.Title) ?? a.Id.Entry;
            return a.Amount != 0 ? $"{title} {a.Amount}" : title;
        }
        catch { return null; }
    }

    public static string CardDescription(CardModel card, PileType pile) =>
        Text.Of(() => card.GetDescriptionForPile(pile)) ?? "";

    /// <summary>One hand card: id, name, cost, type, text, per-target numbers, playability.</summary>
    public static string HandCard(CardModel card, IReadOnlyList<Creature> enemies)
    {
        // With a single enemy the target is implied, so show the numbers against it (Vulnerable etc. applied).
        var impliedTarget = card.TargetType == TargetType.AnyEnemy && enemies.Count == 1 ? enemies[0] : null;
        Preview(card, impliedTarget);
        var sb = new StringBuilder();
        sb.Append(Ids.Card(card)).Append(' ').Append(CardName(card))
          .Append(" [").Append(Cost(card)).Append("] ").Append(card.Type);
        string target = TargetHint(card.TargetType);
        if (target.Length > 0) sb.Append(' ').Append(target);
        sb.Append(" — ").Append(CardDescription(card, PileType.Hand));
        if (impliedTarget != null) Preview(card, null);

        string perTarget = PerTargetValues(card, enemies);
        if (perTarget.Length > 0) sb.Append(" {").Append(perTarget).Append('}');

        try
        {
            if (!card.CanPlay(out var reason, out _)) sb.Append(" ✗").Append(reason);
        }
        catch { /* playability unknown */ }
        return sb.ToString();
    }

    /// <summary>Description after upgrading (for upgrade choices), or null if not upgradable.</summary>
    public static string? UpgradedDescription(CardModel card)
    {
        try
        {
            if (!card.IsUpgradable) return null;
            var preview = card.IsMutable ? (CardModel)card.MutableClone() : card.ToMutable();
            preview.UpgradeInternal();
            return CardDescription(preview, PileType.None);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A card outside the hand (rewards, choices, piles).</summary>
    public static string PlainCard(CardModel card, PileType pile = PileType.None) =>
        $"{CardName(card)} [{Cost(card)}] {card.Type} {card.Rarity} — {CardDescription(card, pile)}";

    private static string TargetHint(TargetType type) => type switch
    {
        TargetType.AnyEnemy => "@enemy",
        TargetType.None or TargetType.Self or TargetType.AllEnemies or TargetType.RandomEnemy => "",
        _ => "@" + type,
    };

    private static void Preview(CardModel card, Creature? target)
    {
        try { card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, card.DynamicVars); }
        catch { /* preview unavailable */ }
    }

    /// <summary>
    /// For enemy-targeted cards with 2+ enemies: the game's computed values per target, only where they differ
    /// (e.g. Vulnerable on one enemy). Restores the untargeted preview afterwards.
    /// </summary>
    private static string PerTargetValues(CardModel card, IReadOnlyList<Creature> enemies)
    {
        if (card.TargetType != TargetType.AnyEnemy || enemies.Count < 2) return "";
        try
        {
            var perEnemy = new List<(Creature Enemy, Dictionary<string, decimal> Values)>();
            foreach (var enemy in enemies)
            {
                Preview(card, enemy);
                perEnemy.Add((enemy, card.DynamicVars.ToDictionary(kv => kv.Key, kv => kv.Value.PreviewValue)));
            }
            Preview(card, null);

            var differing = perEnemy[0].Values.Keys
                .Where(k => perEnemy.Select(p => p.Values.GetValueOrDefault(k)).Distinct().Count() > 1)
                .ToList();
            if (differing.Count == 0) return "";
            return string.Join(" | ", perEnemy.Select(p =>
                Ids.Creature(p.Enemy) + " " + string.Join(" ", differing.Select(k => $"{k} {p.Values[k]:0.##}"))));
        }
        catch
        {
            Preview(card, null);
            return "";
        }
    }

    // ---------- creatures ----------

    public static string Creature(Creature c, bool withIntent)
    {
        var sb = new StringBuilder();
        sb.Append(Ids.Creature(c)).Append(' ').Append(Text.Of(() => c.Name) ?? "?")
          .Append(" HP ").Append(c.CurrentHp).Append('/').Append(c.MaxHp);
        if (c.Block > 0) sb.Append(" blk ").Append(c.Block);
        if (withIntent && c.Monster != null)
        {
            string intent = Intent(c);
            if (intent.Length > 0) sb.Append(" · intent ").Append(intent);
        }
        return sb.ToString();
    }

    public static string Intent(Creature c)
    {
        try
        {
            var monster = c.Monster;
            if (monster?.NextMove == null) return "";
            var targets = c.CombatState?.PlayerCreatures;
            var parts = new List<string>();
            foreach (var intent in monster.NextMove.Intents)
            {
                string? title = null, label = null;
                if (targets != null)
                {
                    try { title = Text.Clean(intent.GetHoverTip(targets, c).Title ?? ""); } catch { }
                    try { label = Text.Of(() => intent.GetIntentLabel(targets, c)); } catch { }
                }
                string part = string.IsNullOrEmpty(title) ? intent.IntentType.ToString() : title;
                if (!string.IsNullOrEmpty(label)) part += " " + label;
                parts.Add(part);
            }
            return string.Join(" + ", parts);
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>Visible powers, one per line: "[name amount] game text".</summary>
    public static IEnumerable<string> Powers(Creature c)
    {
        var lines = new List<string>();
        try
        {
            foreach (var power in c.Powers)
            {
                try
                {
                    if (!power.IsVisible) continue;
                    string name = Text.Of(() => power.Title) ?? power.Id.Entry;
                    string desc = PowerText(power);
                    lines.Add($"[{name} {power.DisplayAmount}] {desc}".TrimEnd());
                }
                catch { /* power in transition */ }
            }
        }
        catch { /* powers unavailable */ }
        return lines;
    }

    private static string PowerText(PowerModel power)
    {
        try
        {
            foreach (var tip in power.HoverTips)
            {
                if (tip.Id == power.Id.ToString() && tip is MegaCrit.Sts2.Core.HoverTips.HoverTip ht && ht.Description != null)
                    return Text.Clean(ht.Description);
            }
        }
        catch { }
        return Text.Of(() => power.SmartDescription) ?? "";
    }

    // ---------- player ----------

    /// <summary>Player line with combat resources (energy, stars, orbs, unknown extras).</summary>
    public static string PlayerCombat(Player me)
    {
        var c = me.Creature;
        var sb = new StringBuilder();
        sb.Append("me ").Append(Text.Of(() => me.Character.Title)).Append(" HP ").Append(c.CurrentHp).Append('/').Append(c.MaxHp);
        if (c.Block > 0) sb.Append(" blk ").Append(c.Block);
        foreach (var resource in Resources.Describe(me))
            sb.Append(" · ").Append(resource);
        return sb.ToString();
    }

    public static string RunHeader(RunState run, Player? me)
    {
        var sb = new StringBuilder();
        sb.Append("Act ").Append(run.CurrentActIndex + 1).Append(" · floor ").Append(SafeInt(() => run.TotalFloor))
          .Append(" · A").Append(run.AscensionLevel);
        if (run.GameMode != GameMode.Standard) sb.Append(" · ").Append(run.GameMode);
        try
        {
            var mods = run.Modifiers.Select(m => Text.Of(() => m.Title) ?? m.Id.Entry).ToList();
            if (mods.Count > 0) sb.Append(" · mods: ").Append(string.Join(", ", mods));
        }
        catch { }
        if (me != null)
        {
            sb.Append(" | ").Append(Text.Of(() => me.Character.Title))
              .Append(" HP ").Append(me.Creature.CurrentHp).Append('/').Append(me.Creature.MaxHp)
              .Append(" · gold ").Append(me.Gold);
        }
        return sb.ToString();
    }

    public static string Relics(Player me)
    {
        var names = me.Relics.Select(r =>
        {
            string name = Text.Of(() => r.Title) ?? r.Id.Entry;
            try { if (r.ShowCounter) name += $"({r.DisplayAmount})"; } catch { }
            return name;
        });
        return "relics: " + string.Join(", ", names);
    }

    public static IEnumerable<string> RelicDetails(Player me) =>
        me.Relics.Select(r => $"{Text.Of(() => r.Title)}: {Text.Of(() => r.DynamicDescription)}");

    public static string Potions(Player me, bool withText)
    {
        var parts = new List<string>();
        for (int i = 0; i < me.PotionSlots.Count; i++)
        {
            var p = me.PotionSlots[i];
            if (p == null) continue;
            string part = $"p{i} {Text.Of(() => p.Title)}";
            if (withText) part += $" ({Text.Of(() => p.DynamicDescription)})";
            parts.Add(part);
        }
        return $"potions {parts.Count}/{me.MaxPotionCount}: " + (parts.Count == 0 ? "-" : string.Join(", ", parts));
    }

    public static string Deck(Player me)
    {
        var groups = me.Deck.Cards
            .GroupBy(CardLabel)
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
        return $"deck ({me.Deck.Cards.Count}): " + string.Join(", ", groups);
    }

    private static int SafeInt(Func<int> get)
    {
        try { return get(); } catch { return -1; }
    }
}

/// <summary>
/// Character resources shown on the player line. Known resources have providers; any other public scalar on
/// PlayerCombatState is shown raw, so a new character's resource is visible before a provider is written.
/// </summary>
internal static class Resources
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "Energy", "MaxEnergy", "Stars", "TurnNumber", "Phase",
    };

    private static readonly List<Func<Player, string?>> Providers = new()
    {
        Energy,
        Stars,
        Orbs,
    };

    public static IEnumerable<string> Describe(Player me)
    {
        foreach (var provider in Providers)
        {
            string? text = null;
            try { text = provider(me); } catch { }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
        string? extras = null;
        try { extras = Extras(me); } catch { }
        if (!string.IsNullOrEmpty(extras)) yield return extras;
    }

    private static string? Energy(Player me)
    {
        var pcs = me.PlayerCombatState;
        if (pcs == null || !CombatManager.Instance.IsInProgress) return null;
        return $"energy {pcs.Energy}/{pcs.MaxEnergy}";
    }

    private static string? Stars(Player me)
    {
        var pcs = me.PlayerCombatState;
        if (pcs == null || !CombatManager.Instance.IsInProgress) return null;
        return me.Character.ShouldAlwaysShowStarCounter || pcs.Stars > 0 ? $"stars {pcs.Stars}" : null;
    }

    private static string? Orbs(Player me)
    {
        var queue = me.PlayerCombatState?.OrbQueue;
        if (queue == null || queue.Capacity <= 0 || !CombatManager.Instance.IsInProgress) return null;
        var orbs = queue.Orbs.Select(o => $"{Text.Of(() => o.Title)}({o.PassiveVal:0.##}/{o.EvokeVal:0.##})");
        return $"orbs {queue.Orbs.Count}/{queue.Capacity}: " + string.Join(", ", orbs);
    }

    /// <summary>Unknown public int/bool/decimal properties of PlayerCombatState with non-default values.</summary>
    private static string? Extras(Player me)
    {
        var pcs = me.PlayerCombatState;
        if (pcs == null || !CombatManager.Instance.IsInProgress) return null;
        var parts = new List<string>();
        foreach (var prop in pcs.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (Known.Contains(prop.Name) || prop.GetIndexParameters().Length > 0) continue;
            var type = prop.PropertyType;
            if (type != typeof(int) && type != typeof(bool) && type != typeof(decimal)) continue;
            object? value;
            try { value = prop.GetValue(pcs); } catch { continue; }
            if (value is 0 or false || value is decimal d && d == 0) continue;
            parts.Add($"{prop.Name}={value}");
        }
        return parts.Count == 0 ? null : "extras " + string.Join(" ", parts);
    }
}
