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
        AddAll(() => ModelDb.AllEncounters, "encounters");
        AddAll(() => ModelDb.AllEvents, "events");
        AddAll(() => ModelDb.AllAncients, "ancients");
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
            // Enemies in the current fight by id (e1, e2...): live pattern with history and upcoming moves.
            var ctx = GameCtx.Capture();
            if (ctx.InCombat && query.TrimStart('@').StartsWith('e')
                && Ids.ResolveCreature(ctx.Combat, ctx.Me?.Creature, query) is { Monster: not null } enemy)
            {
                sb.AppendLine(WithKb(MonsterInfo.DescribeLive(enemy), enemy.Monster));
                continue;
            }

            // "boss": the current act's boss encounter(s).
            if (query.Equals("boss", StringComparison.OrdinalIgnoreCase))
            {
                if (ctx.Run == null)
                {
                    sb.AppendLine("boss: no run in progress (look up an encounter or monster by name instead)");
                    continue;
                }
                var act = ctx.Run.Act;
                foreach (var encounter in new[] { Try(() => act.BossEncounter), Try(() => act.SecondBossEncounter) })
                    if (encounter != null) sb.AppendLine(MonsterInfo.DescribeEncounter(encounter));
                continue;
            }

            var matches = Find(query);
            // An encounter already lists its monsters; drop separate matches for the same monsters.
            var covered = matches.Select(m => m.Model).OfType<EncounterModel>()
                .SelectMany(e => Try(() => e.AllPossibleMonsters.ToList()) ?? new List<MonsterModel>())
                .Select(m => m.Id.ToString()).ToHashSet();
            matches = matches.Where(m => m.Model is not MonsterModel || !covered.Contains(m.Id)).ToList();
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

    /// <summary>Appends the knowledge-base document for the model, if there is one.</summary>
    public static string WithKb(string description, AbstractModel model) =>
        KbDocs.For(model) is { } doc ? $"{description}\n--- kb ---\n{doc}" : description;

    private static T? Try<T>(Func<T?> get) where T : class
    {
        try { return get(); } catch { return null; }
    }

    public static string? TitleOf(AbstractModel model) => model switch
    {
        CardModel c => Text.Of(() => c.Title),
        RelicModel r => Text.Of(() => r.Title),
        PotionModel p => Text.Of(() => p.Title),
        PowerModel p => Text.Of(() => p.Title),
        MonsterModel m => Text.Of(() => m.Title),
        EncounterModel e => Text.Of(() => e.Title),
        EventModel e => Text.Of(() => e.Title),
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
            case MonsterModel monster:
                return WithKb(MonsterInfo.Describe(monster), monster);
            case EncounterModel encounter:
                return MonsterInfo.DescribeEncounter(encounter);
            case EventModel evt:
                return WithKb($"{id} {title}", evt);
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
