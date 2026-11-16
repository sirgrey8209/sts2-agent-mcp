using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Game;

/// <summary>Lookup of canonical game models (ModelDb) for the `info` tool.</summary>
internal static class Catalog
{
    private sealed record Entry(string Id, string EntryName, string? Title, AbstractModel Model);

    private static List<Entry>? _index;

    private static List<Entry> Index => _index ??= Build();

    private static List<Entry> Build()
    {
        var models = new List<AbstractModel>();
        void AddAll(Func<IEnumerable<AbstractModel>> source, string what)
        {
            try { models.AddRange(source()); }
            catch (Exception ex) { Host.Log.Warn($"catalog: could not enumerate {what}: {ex.Message}"); }
        }
        AddAll(() => ModelDb.AllCards, "cards");
        AddAll(() => ModelDb.AllRelics, "relics");
        AddAll(() => ModelDb.AllPotions, "potions");
        AddAll(() => ModelDb.AllPowers, "powers");
        AddAll(() => ModelDb.Monsters, "monsters");
        AddAll(() => ModelDb.AllCharacters, "characters");

        return models
            .GroupBy(m => m.Id.ToString())
            .Select(g => g.First())
            .Select(m => new Entry(m.Id.ToString(), m.Id.Entry, TitleOf(m), m))
            .ToList();
    }

    /// <summary>A canonical card by id ("CARD.BASH", "BASH") or displayed name, or null.</summary>
    public static CardModel? FindCard(string query) =>
        Find(query).Select(e => e.Model).OfType<CardModel>().FirstOrDefault();

    public static string Describe(IReadOnlyList<string> queries)
    {
        var sb = new StringBuilder();
        foreach (var query in queries)
        {
            var matches = Find(query);
            if (matches.Count == 0)
            {
                sb.AppendLine($"{query}: not found");
                continue;
            }
            foreach (var entry in matches)
                sb.AppendLine(Describe(entry.Model));
        }
        return sb.ToString().TrimEnd();
    }

    private static List<Entry> Find(string query)
    {
        string q = query.Trim();
        var exact = Index.Where(e =>
            e.Id.Equals(q, StringComparison.OrdinalIgnoreCase) ||
            e.EntryName.Equals(q, StringComparison.OrdinalIgnoreCase) ||
            (e.Title != null && e.Title.Equals(q, StringComparison.OrdinalIgnoreCase))).ToList();
        if (exact.Count > 0) return exact.Take(5).ToList();

        return Index.Where(e =>
                e.EntryName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (e.Title != null && e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToList();
    }

    public static string? TitleOf(AbstractModel model) => model switch
    {
        CardModel c => Text.Of(() => c.Title),
        RelicModel r => Text.Of(() => r.Title),
        PotionModel p => Text.Of(() => p.Title),
        PowerModel p => Text.Of(() => p.Title),
        MonsterModel m => Text.Of(() => m.Title),
        CharacterModel c => Text.Of(() => c.Title),
        _ => null,
    };

    private static string Describe(AbstractModel model)
    {
        string id = model.Id.ToString();
        string title = TitleOf(model) ?? model.Id.Entry;
        switch (model)
        {
            case CardModel card:
                return $"{id} {title} [{Fmt.Cost(card)}] {card.Type} {card.Rarity}: "
                       + (Text.Of(() => card.GetDescriptionForPile(PileType.None)) ?? "")
                       + Tips(card.HoverTips);
            case RelicModel relic:
                return $"{id} {title}: {Text.Of(() => relic.DynamicDescription)}" + Tips(relic.HoverTipsExcludingRelic);
            case PotionModel potion:
                return $"{id} {title} ({potion.Usage}, {potion.TargetType}): {Text.Of(() => potion.DynamicDescription)}" + Tips(potion.ExtraHoverTips);
            case PowerModel power:
                return $"{id} {title} ({power.Type}): {Text.Of(() => power.Description)}";
            case CharacterModel character:
                return $"{id} {title}: HP {character.StartingHp}, gold {character.StartingGold}, energy {character.MaxEnergy}";
            default:
                return $"{id} {title}";
        }
    }

    private static string Tips(IEnumerable<IHoverTip> tips)
    {
        try
        {
            var parts = new List<string>();
            foreach (var tip in IHoverTip.RemoveDupes(tips))
            {
                if (tip is HoverTip ht && ht.Title != null)
                    parts.Add($"{Text.Clean(ht.Title)}: {Text.Clean(ht.Description ?? "")}");
            }
            return parts.Count == 0 ? "" : "\n  · " + string.Join("\n  · ", parts);
        }
        catch
        {
            return "";
        }
    }
}
