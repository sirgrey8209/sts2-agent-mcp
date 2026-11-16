using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Screens;

internal sealed class CombatHandler : IScreenHandler
{
    public string Kind => "combat";

    public bool Matches(GameCtx ctx) => ctx.InCombat && ctx.Screen is NCombatRoom && ctx.Me != null;

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var combat = ctx.Combat;
        var me = ctx.Me!;
        var pcs = me.PlayerCombatState;
        if (combat == null || pcs == null) return view.Add("combat state unavailable");

        bool myTurn = combat.CurrentSide == CombatSide.Player;
        view.Add($"round {combat.RoundNumber} · {(myTurn ? "your turn" : "enemy turn")}");

        view.Add(Fmt.PlayerCombat(me));
        view.AddRange(Fmt.Powers(me.Creature), "  ");
        foreach (var pet in pcs.Pets.Where(p => p.IsAlive))
        {
            view.Add("  " + Fmt.Creature(pet, withIntent: false));
            view.AddRange(Fmt.Powers(pet), "    ");
        }

        var enemies = CombatTargets.Enemies(combat);
        view.Add("hand:");
        foreach (var card in pcs.Hand.Cards)
            view.Add("  " + Fmt.HandCard(card, enemies));

        view.Add($"piles: draw {pcs.DrawPile.Cards.Count} · discard {pcs.DiscardPile.Cards.Count} · exhaust {pcs.ExhaustPile.Cards.Count}");
        if (full)
        {
            view.Add("  draw (unordered): " + string.Join(", ", pcs.DrawPile.Cards.Select(Fmt.CardName).OrderBy(n => n)));
            view.Add("  discard: " + string.Join(", ", pcs.DiscardPile.Cards.Select(Fmt.CardName)));
            view.Add("  exhaust: " + string.Join(", ", pcs.ExhaustPile.Cards.Select(Fmt.CardName)));
        }

        view.Add("enemies:");
        foreach (var enemy in enemies)
        {
            view.Add("  " + Fmt.Creature(enemy, withIntent: true));
            view.AddRange(Fmt.Powers(enemy), "    ");
        }

        view.Actions = myTurn
            ? "play <card> [@<target>] · potion <slot> [@<target>] · discard_potion <slot> · end"
            : "wait (enemy turn in progress)";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var me = ctx.Me!;
        var combat = ctx.Combat;
        if (combat == null) return ExecResult.Fail("no combat state");

        switch (action.Verb)
        {
            case "play":
                return Play(ctx, action);
            case "end":
                if (combat.CurrentSide != CombatSide.Player) return ExecResult.Fail("not your turn");
                if (CombatManager.Instance.PlayerActionsDisabled) return ExecResult.Fail("player actions are disabled right now");
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, me.PlayerCombatState!.TurnNumber));
                return ExecResult.Ok(SettleMode.TurnEnd);
            default:
                return PotionActions.Handles(action.Verb)
                    ? PotionActions.Execute(ctx, action)
                    : ExecResult.Fail($"unknown combat action '{action.Verb}'");
        }
    }

    private static ExecResult Play(GameCtx ctx, ActionSpec action)
    {
        var me = ctx.Me!;
        if (ctx.Combat!.CurrentSide != CombatSide.Player) return ExecResult.Fail("not your turn");
        string? token = action.Arg(0);
        if (token == null) return ExecResult.Fail("usage: play <card id> [@<target>]");
        var card = Ids.ResolveCard(token);
        if (card == null || !me.PlayerCombatState!.Hand.Cards.Contains(card))
            return ExecResult.Fail($"{token} is not in your hand");

        if (!card.CanPlay(out var reason, out _))
            return ExecResult.Fail($"{Fmt.CardName(card)} cannot be played: {reason}");

        Creature? target = null;
        if (card.TargetType == TargetType.AnyEnemy || action.Target != null)
        {
            bool enemyOnly = card.TargetType == TargetType.AnyEnemy;
            target = CombatTargets.Resolve(ctx, action.Target, enemyOnly, out string? error);
            if (target == null) return ExecResult.Fail(error!);
        }

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target));
        return ExecResult.Ok(SettleMode.Combat);
    }
}
