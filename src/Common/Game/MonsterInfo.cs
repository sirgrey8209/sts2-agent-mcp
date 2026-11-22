using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Monster move patterns read from the game's own move state machine (the same one combat uses), so bosses and
/// elites can be studied without reading code. Attack numbers are base values for the current run's ascension
/// (Strength and other modifiers not applied). Conditional branches list their candidates; the conditions themselves
/// are code and cannot be described.
/// </summary>
internal static class MonsterInfo
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Pattern of a monster type (canonical model).</summary>
    public static string Describe(MonsterModel monster)
    {
        var sb = new StringBuilder();
        sb.Append($"{monster.Id} {Text.Of(() => monster.Title)} · HP {Safe(() => monster.MinInitialHp)}");
        int max = Safe(() => monster.MaxInitialHp);
        if (max != Safe(() => monster.MinInitialHp)) sb.Append($"-{max}");
        sb.AppendLine();

        MonsterMoveStateMachine? machine;
        try
        {
            var instance = monster.IsMutable ? monster : monster.ToMutable();
            machine = typeof(MonsterModel).GetMethod("GenerateMoveStateMachine", Any)?.Invoke(instance, null) as MonsterMoveStateMachine;
        }
        catch (Exception ex)
        {
            return sb.AppendLine($"  (pattern unavailable: {Inner(ex).Message})").ToString().TrimEnd();
        }
        if (machine == null) return sb.AppendLine("  (pattern unavailable)").ToString().TrimEnd();

        AppendPattern(sb, monster, machine, owner: null);
        return sb.ToString().TrimEnd();
    }

    /// <summary>A monster in the current fight: move history, the announced move and what follows it.</summary>
    public static string DescribeLive(Creature creature)
    {
        var monster = creature.Monster!;
        var machine = monster.MoveStateMachine;
        var sb = new StringBuilder();
        sb.AppendLine($"{Ids.Creature(creature)} {Text.Of(() => creature.Name)} ({monster.Id}) HP {creature.CurrentHp}/{creature.MaxHp}");
        if (machine == null) return sb.AppendLine("  (no move state machine)").ToString().TrimEnd();

        var history = machine.StateLog.OfType<MoveState>().Select(m => MoveName(monster, m.Id)).ToList();
        sb.AppendLine("  moves so far: " + (history.Count == 0 ? "-" : string.Join(" → ", history.TakeLast(12))));

        var next = monster.NextMove;
        if (next != null)
        {
            sb.AppendLine($"  next: {MoveLine(monster, next, creature)}");
            sb.AppendLine("  after that: " + Upcoming(monster, machine, next));
        }

        sb.AppendLine("  full pattern:");
        AppendPattern(sb, monster, machine, creature);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Monsters that can appear in an encounter (e.g. the act boss), each with its pattern.</summary>
    public static string DescribeEncounter(EncounterModel encounter)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{encounter.Id} {Text.Of(() => encounter.Title)} — possible monsters:");
        List<MonsterModel> monsters;
        try { monsters = encounter.AllPossibleMonsters.Distinct().ToList(); }
        catch (Exception ex) { return sb.AppendLine($"  (unavailable: {ex.Message})").ToString().TrimEnd(); }
        foreach (var monster in monsters)
            sb.AppendLine(Catalog.WithKb(Describe(monster), monster));
        return sb.ToString().TrimEnd();
    }

    private static void AppendPattern(StringBuilder sb, MonsterModel monster, MonsterMoveStateMachine machine, Creature? owner)
    {
        var initial = Get<MonsterState>(machine, "_initialState");
        if (initial != null) sb.AppendLine($"  start: {StateName(monster, initial.Id, machine)}");
        foreach (var state in machine.States.Values)
        {
            switch (state)
            {
                case MoveState move:
                    string follow = move.FollowUpState?.Id ?? move.FollowUpStateId ?? "";
                    sb.Append($"  {MoveLine(monster, move, owner)}");
                    if (follow.Length > 0) sb.Append($" → {StateName(monster, follow, machine)}");
                    if (move.MustPerformOnceBeforeTransitioning) sb.Append(" (must complete)");
                    sb.AppendLine();
                    break;
                case RandomBranchState random:
                    var options = random.States.Select(w =>
                    {
                        var parts = new List<string>();
                        float weight = SafeF(w.GetWeight);
                        if (!float.IsNaN(weight)) parts.Add($"w{weight:0.##}");
                        if (w.cooldown > 0) parts.Add($"cooldown {w.cooldown}");
                        parts.Add(w.repeatType switch
                        {
                            MoveRepeatType.CannotRepeat => "no repeat",
                            MoveRepeatType.UseOnlyOnce => "once",
                            MoveRepeatType.CanRepeatXTimes => $"max {w.maxTimes} in a row",
                            _ => "",
                        });
                        return $"{StateName(monster, w.stateId, machine)} ({string.Join(", ", parts.Where(p => p.Length > 0))})";
                    });
                    sb.AppendLine($"  [{random.Id}] random: {string.Join(" | ", options)}");
                    break;
                case ConditionalBranchState conditional:
                    var ids = (Get<IEnumerable>(conditional, "States") ?? Array.Empty<object>())
                        .Cast<object>()
                        .Select(b => b.GetType().GetField("id")?.GetValue(b) as string ?? "?");
                    sb.AppendLine($"  [{conditional.Id}] conditional: {string.Join(" | ", ids.Select(i => StateName(monster, i, machine)))}");
                    break;
            }
        }
    }

    /// <summary>Deterministic follow-ups of a move until a branch is reached.</summary>
    private static string Upcoming(MonsterModel monster, MonsterMoveStateMachine machine, MoveState from)
    {
        var parts = new List<string>();
        MonsterState? state = from.FollowUpState ?? (from.FollowUpStateId != null && machine.States.TryGetValue(from.FollowUpStateId, out var s) ? s : null);
        for (int i = 0; i < 4 && state != null; i++)
        {
            if (state is MoveState move)
            {
                parts.Add(MoveName(monster, move.Id));
                state = move.FollowUpState ?? (move.FollowUpStateId != null && machine.States.TryGetValue(move.FollowUpStateId, out var n) ? n : null);
            }
            else
            {
                parts.Add($"[{state.Id}: branch]");
                break;
            }
        }
        return parts.Count == 0 ? "unknown" : string.Join(" → ", parts);
    }

    private static string MoveLine(MonsterModel monster, MoveState move, Creature? owner) =>
        $"{MoveName(monster, move.Id)}: {string.Join(" + ", move.Intents.Select(i => IntentText(i, owner)))}";

    private static string IntentText(AbstractIntent intent, Creature? owner)
    {
        if (intent is AttackIntent attack)
        {
            string damage;
            try { damage = attack.DamageCalc != null ? $"{attack.DamageCalc():0}" : "?"; }
            catch { damage = "?"; }
            int repeats = Safe(() => attack.Repeats);
            return $"Attack {damage}" + (repeats > 1 ? $"x{repeats}" : "");
        }
        return intent.IntentType.ToString();
    }

    private static string StateName(MonsterModel monster, string id, MonsterMoveStateMachine machine) =>
        machine.States.TryGetValue(id, out var state) && state is MoveState ? MoveName(monster, id) : $"[{id}]";

    /// <summary>Localized move name from the bestiary table, with the internal id.</summary>
    private static string MoveName(MonsterModel monster, string moveId)
    {
        string key = moveId.EndsWith("_MOVE", StringComparison.Ordinal) ? moveId[..^5] : moveId;
        try
        {
            var loc = new LocString("monsters", monster.Id.Entry + ".moves." + key + ".title");
            if (loc.Exists()) return $"{Text.Clean(loc.GetFormattedText())}({moveId})";
        }
        catch { /* no localized name */ }
        return moveId;
    }

    private static T? Get<T>(object source, string member) where T : class
    {
        var type = source.GetType();
        return (type.GetField(member, Any)?.GetValue(source) ?? type.GetProperty(member, Any)?.GetValue(source)) as T;
    }

    private static int Safe(Func<int> get)
    {
        try { return get(); } catch { return -1; }
    }

    private static float SafeF(Func<float> get)
    {
        try { return get(); } catch { return float.NaN; }
    }

    private static Exception Inner(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
}
