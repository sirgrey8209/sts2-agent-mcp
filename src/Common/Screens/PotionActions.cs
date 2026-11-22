using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>
/// Potion use/discard is available on every in-run screen, not just combat
/// (e.g. drinking a heal potion at the map, discarding to make room for a reward).
/// </summary>
internal static class PotionActions
{
    public static bool Handles(string verb) => verb is "potion" or "discard_potion";

    public static bool HasPotions(GameCtx ctx) => ctx.Me?.PotionSlots.Any(p => p != null) ?? false;

    public static ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        if (ctx.Me == null) return ExecResult.Fail("no run in progress");
        return action.Verb == "potion" ? Use(ctx, action) : Discard(ctx, action);
    }

    private static ExecResult Use(GameCtx ctx, ActionSpec action)
    {
        var me = ctx.Me!;
        var potion = At(me, action.Arg(0), out string? slotError);
        if (potion == null) return ExecResult.Fail(slotError!);
        if (potion.IsQueued) return ExecResult.Fail("that potion is already being used");
        if (potion.Usage == PotionUsage.Automatic) return ExecResult.Fail("that potion is used automatically");
        if (potion.Usage == PotionUsage.CombatOnly && !ctx.InCombat) return ExecResult.Fail("that potion can only be used in combat");
        if (!potion.PassesCustomUsabilityCheck) return ExecResult.Fail("that potion cannot be used right now");
        if (ctx.InCombat)
        {
            if (ctx.Combat?.CurrentSide != CombatSide.Player) return ExecResult.Fail("not your turn");
            if (CombatManager.Instance.PlayerActionsDisabled) return ExecResult.Fail("player actions are disabled right now");
        }

        Creature? target;
        switch (potion.TargetType)
        {
            case TargetType.AnyEnemy:
                target = CombatTargets.Resolve(ctx, action.Target, onlyEnemies: true, out string? error);
                if (target == null) return ExecResult.Fail(error!);
                break;
            case TargetType.Self:
            case TargetType.AnyPlayer:
                target = me.Creature;
                break;
            case TargetType.AnyAlly:
                target = action.Target == null ? me.Creature : CombatTargets.Resolve(ctx, action.Target, onlyEnemies: false, out _);
                if (target == null) return ExecResult.Fail($"unknown target @{action.Target}");
                break;
            default:
                target = null;
                break;
        }

        potion.EnqueueManualUse(target);
        return ExecResult.Ok(ctx.InCombat ? SettleMode.Combat : SettleMode.Screen);
    }

    private static ExecResult Discard(GameCtx ctx, ActionSpec action)
    {
        var potion = At(ctx.Me!, action.Arg(0), out string? error);
        if (potion == null) return ExecResult.Fail(error!);
        _ = PotionCmd.Discard(potion);
        return ExecResult.Ok(ctx.InCombat ? SettleMode.Combat : SettleMode.Screen);
    }

    private static PotionModel? At(Player me, string? token, out string? error)
    {
        error = null;
        string digits = token?.TrimStart('p') ?? "";
        if (!int.TryParse(digits, out int slot) || slot < 0 || slot >= me.PotionSlots.Count)
        {
            error = $"usage: potion p<slot> (slots 0-{me.PotionSlots.Count - 1})";
            return null;
        }
        var potion = me.PotionSlots[slot];
        if (potion == null) error = $"slot p{slot} is empty";
        return potion;
    }
}

/// <summary>Target resolution shared by cards and potions.</summary>
internal static class CombatTargets
{
    public static System.Collections.Generic.List<Creature> Enemies(CombatState combat) =>
        combat.Enemies.Where(e => e.IsAlive).ToList();

    public static Creature? Resolve(GameCtx ctx, string? token, bool onlyEnemies, out string? error)
    {
        error = null;
        if (ctx.Combat == null)
        {
            error = "targets only exist in combat";
            return null;
        }
        if (token == null)
        {
            var alive = Enemies(ctx.Combat);
            if (onlyEnemies && alive.Count == 1) return alive[0];
            error = "this needs a target, e.g. @" + (alive.Count > 0 ? Ids.Creature(alive[0]) : "e1");
            return null;
        }
        var creature = Ids.ResolveCreature(ctx.Combat, ctx.Me?.Creature, token);
        if (creature == null || !creature.IsAlive)
        {
            error = $"no living target @{token}";
            return null;
        }
        if (onlyEnemies && (creature.Player != null || creature.IsPet))
        {
            error = $"@{token} is not an enemy";
            return null;
        }
        return creature;
    }
}
