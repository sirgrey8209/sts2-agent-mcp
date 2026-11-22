using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Game;

/// <summary>Filters for <see cref="Search.Run"/>. Null fields do not filter.</summary>
internal sealed record SearchQuery(
    string? Text,
    string? Kind,
    string? Pool,
    string? Type,
    string? Rarity,
    string? Cost,
    string? Keyword,
    bool Upgraded,
    int Limit);

/// <summary>
/// Search over the canonical cards, relics and potions (ModelDb): free text over name and description, plus filters.
/// Descriptions are the game's own text for the canonical (out-of-run) model.
/// </summary>
internal static class Search
{
    private sealed record Item(
        string Kind, AbstractModel Model, string Id, string Title, string Pool,
        string? Type, string Rarity, int RarityRank, string? Cost, IReadOnlyCollection<string> Keywords, string Description);

    private static List<Item>? _items;
    private static readonly Dictionary<string, Item?> UpgradedCache = new();

    // Pools that hold removed or test content; searched only when asked for by name.
    private static readonly HashSet<string> HiddenPools = new(StringComparer.OrdinalIgnoreCase) { "deprecated", "mock" };

    public static string Run(SearchQuery q)
    {
        var items = _items ??= Build();
        string[] terms = (q.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pools = ResolvePools(q.Pool);

        var matches = new List<Item>();
        foreach (var baseItem in items)
        {
            if (q.Kind != null && !baseItem.Kind.Equals(q.Kind, StringComparison.OrdinalIgnoreCase)) continue;
            if (pools != null ? !pools.Contains(baseItem.Pool) : HiddenPools.Contains(baseItem.Pool)) continue;
            if (q.Type != null && !string.Equals(baseItem.Type, q.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (q.Rarity != null && !baseItem.Rarity.Equals(q.Rarity, StringComparison.OrdinalIgnoreCase)) continue;
            if (q.Keyword != null && !baseItem.Keywords.Contains(q.Keyword, StringComparer.OrdinalIgnoreCase)) continue;

            var item = baseItem;
            if (q.Upgraded && baseItem.Model is CardModel)
            {
                item = Upgraded(baseItem);
                if (item == null) continue; // not upgradable
            }
            if (q.Cost != null && !CostMatches(item.Cost, q.Cost)) continue;
            if (terms.Length > 0)
            {
                string haystack = $"{item.Title} {item.Description} {item.Id}";
                if (!terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
            }
            matches.Add(item);
        }

        if (matches.Count == 0) return "no matches";
        var sb = new StringBuilder();
        int limit = Math.Clamp(q.Limit, 1, 200);
        sb.AppendLine(matches.Count > limit
            ? $"{matches.Count} matches (showing {limit}; narrow the search or raise limit)"
            : $"{matches.Count} match{(matches.Count == 1 ? "" : "es")}");
        foreach (var item in matches.Take(limit))
            sb.AppendLine(Line(item));
        return sb.ToString().TrimEnd();
    }

    private static string Line(Item item)
    {
        var sb = new StringBuilder();
        sb.Append(item.Id).Append(' ').Append(item.Title);
        if (item.Cost != null) sb.Append(" [").Append(item.Cost).Append(']');
        if (item.Type != null) sb.Append(' ').Append(item.Type);
        sb.Append(' ').Append(item.Rarity).Append(" (").Append(item.Pool).Append("): ").Append(item.Description);
        return sb.ToString();
    }

    private static List<Item> Build()
    {
        var list = new List<Item>();
        void Add(string what, Func<IEnumerable<Item>> make)
        {
            try { list.AddRange(make()); }
            catch (Exception ex) { Host.Log.Warn($"search: could not index {what}: {ex.Message}"); }
        }
        Add("cards", () => ModelDb.AllCards.Select(CardItem).OfType<Item>());
        Add("relics", () => ModelDb.AllRelics.Select(r => SafeItem(() => new Item(
            "relic", r, r.Id.ToString(), Catalog.TitleOf(r) ?? r.Id.Entry, PoolName(() => r.Pool),
            null, r.Rarity.ToString(), (int)r.Rarity, null, Array.Empty<string>(), Text.Of(() => r.DynamicDescription) ?? ""))).OfType<Item>());
        Add("potions", () => ModelDb.AllPotions.Select(p => SafeItem(() => new Item(
            "potion", p, p.Id.ToString(), Catalog.TitleOf(p) ?? p.Id.Entry, PoolName(() => p.Pool),
            null, p.Rarity.ToString(), (int)p.Rarity, null, Array.Empty<string>(),
            $"({p.Usage}, {p.TargetType}) {Text.Of(() => p.DynamicDescription)}"))).OfType<Item>());
        return list
            .GroupBy(i => i.Id).Select(g => g.First())
            .OrderBy(i => KindOrder(i.Kind)).ThenBy(i => i.Pool).ThenBy(i => i.RarityRank).ThenBy(i => i.Title)
            .ToList();
    }

    private static Item? CardItem(CardModel card) => SafeItem(() => new Item(
        "card", card, card.Id.ToString(), Catalog.TitleOf(card) ?? card.Id.Entry, PoolName(() => card.Pool),
        card.Type.ToString(), card.Rarity.ToString(), (int)card.Rarity, Fmt.Cost(card),
        card.CanonicalKeywords.Select(k => k.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
        Text.Of(() => card.GetDescriptionForPile(PileType.None)) ?? ""));

    /// <summary>The upgraded form of a card item, or null when the card cannot be upgraded.</summary>
    private static Item? Upgraded(Item item)
    {
        if (UpgradedCache.TryGetValue(item.Id, out var cached)) return cached;
        Item? result = null;
        if (UpgradedCard((CardModel)item.Model) is { } up)
            result = item with { Title = Catalog.TitleOf(up) ?? item.Title + "+", Cost = Fmt.Cost(up), Description = UpgradedText(up) };
        UpgradedCache[item.Id] = result;
        return result;
    }

    /// <summary>A mutable upgraded copy of a canonical card (the card library's upgrade preview), or null.</summary>
    public static CardModel? UpgradedCard(CardModel card)
    {
        try
        {
            if (!card.IsUpgradable) return null;
            var up = (CardModel)card.MutableClone();
            up.UpgradeInternal();
            up.UpdateDynamicVarPreview(CardPreviewMode.Upgrade, null, card.DynamicVars);
            return up;
        }
        catch (Exception ex)
        {
            Host.Log.Warn($"search: could not upgrade {card.Id}: {ex.Message}");
            return null;
        }
    }

    public static string UpgradedText(CardModel upgraded) => Text.Of(() => upgraded.GetDescriptionForUpgradePreview()) ?? "";

    private static bool CostMatches(string? cost, string wanted)
    {
        if (cost == null) return false;
        // Fmt.Cost: "1E", "XE", "2E 1★"
        string energy = cost.Split(' ')[0].TrimEnd('E');
        return energy.Equals(wanted.Trim().TrimEnd('E', 'e'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pool names matching a pool or character name (DEFECT, 디펙트, colorless, shared...), or null for no filter.</summary>
    private static HashSet<string>? ResolvePools(string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted)) return null;
        string w = wanted.Trim();
        var pools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { w };
        try
        {
            foreach (var c in ModelDb.AllCharacters)
            {
                if (!c.Id.Entry.Equals(w, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Catalog.TitleOf(c), w, StringComparison.OrdinalIgnoreCase)) continue;
                pools.Add(PoolName(() => c.CardPool));
                pools.Add(PoolName(() => c.RelicPool));
                pools.Add(PoolName(() => c.PotionPool));
            }
        }
        catch { /* character lookup is a convenience */ }
        return pools;
    }

    /// <summary>"DEFECT_CARD_POOL" → "defect", "SHARED_RELIC_POOL" → "shared".</summary>
    private static string PoolName(Func<AbstractModel> pool)
    {
        try
        {
            string entry = pool().Id.Entry;
            foreach (var suffix in new[] { "_CARD_POOL", "_RELIC_POOL", "_POTION_POOL", "_POOL" })
                if (entry.EndsWith(suffix, StringComparison.Ordinal)) { entry = entry[..^suffix.Length]; break; }
            return entry.ToLowerInvariant();
        }
        catch
        {
            return "none";
        }
    }

    private static int KindOrder(string kind) => kind switch { "card" => 0, "relic" => 1, _ => 2 };

    private static Item? SafeItem(Func<Item> make)
    {
        try { return make(); } catch { return null; }
    }
}
