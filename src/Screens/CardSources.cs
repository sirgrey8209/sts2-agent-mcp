using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>Which cards to draw for render_cards. Captions match the ids/indices used by `act`.</summary>
internal static class CardSources
{
    public const string Help = "hand, draw, discard, exhaust, deck, choice, screen (card reward / shop / choice on screen)";

    /// <summary>Main thread.</summary>
    public static List<SheetCard> FromSource(GameCtx ctx, string source)
    {
        var me = ctx.Me;
        var pcs = me?.PlayerCombatState;
        switch (source)
        {
            case "hand":
                Require(ctx.InCombat && pcs != null, "not in combat");
                return pcs!.Hand.Cards.Select(c => new SheetCard(c, Ids.Card(c), PileType.Hand)).ToList();
            case "draw":
                Require(ctx.InCombat && pcs != null, "not in combat");
                // Draw pile order is hidden information: sort by name.
                return pcs!.DrawPile.Cards.OrderBy(View.Fmt.CardName).Select(c => new SheetCard(c, null, PileType.Draw)).ToList();
            case "discard":
                Require(ctx.InCombat && pcs != null, "not in combat");
                return pcs!.DiscardPile.Cards.Select(c => new SheetCard(c, null, PileType.Discard)).ToList();
            case "exhaust":
                Require(ctx.InCombat && pcs != null, "not in combat");
                return pcs!.ExhaustPile.Cards.Select(c => new SheetCard(c, null, PileType.Exhaust)).ToList();
            case "deck":
                Require(me != null, "no run in progress");
                return me!.Deck.Cards.Select(c => new SheetCard(c, null, PileType.Deck)).ToList();
            case "choice":
                return Choice() ?? throw new Tools.ToolError("no card choice is pending");
            case "screen":
                return Choice() ?? CardReward(ctx) ?? Shop(ctx)
                       ?? throw new Tools.ToolError("no cards on this screen (use a source such as hand/deck, or card ids)");
            default:
                throw new Tools.ToolError($"unknown source '{source}' (use {Help})");
        }
    }

    /// <summary>Main thread. "c12" = combat card id; anything else is looked up in the card catalog.</summary>
    public static List<SheetCard> FromIds(IEnumerable<string> ids)
    {
        var result = new List<SheetCard>();
        foreach (var id in ids)
        {
            var combatCard = Ids.ResolveCard(id);
            if (combatCard != null)
            {
                result.Add(new SheetCard(combatCard, id, combatCard.Pile?.Type ?? PileType.None));
                continue;
            }
            var card = Catalog.FindCard(id) ?? throw new Tools.ToolError($"card '{id}' not found");
            result.Add(new SheetCard(card, null, PileType.None));
        }
        return result;
    }

    private static List<SheetCard>? Choice()
    {
        var choice = ChoiceBroker.Instance.Pending;
        return choice?.Options.Select((c, i) => new SheetCard(c, i.ToString(), c.Pile?.Type ?? PileType.None)).ToList();
    }

    private static List<SheetCard>? CardReward(GameCtx ctx)
    {
        if (ctx.Screen is not NCardRewardSelectionScreen screen) return null;
        return Ui.FindAllByPosition<NCardHolder>(screen)
            .Where(h => h.CardModel != null && Ui.IsVisible(h))
            .Select((h, i) => new SheetCard(h.CardModel!, i.ToString(), PileType.None))
            .ToList();
    }

    private static List<SheetCard>? Shop(GameCtx ctx)
    {
        MerchantInventory? inventory = (ctx.Run?.CurrentRoom as MerchantRoom)?.GetLocalInventory();
        if (inventory == null || !new ShopHandler().Matches(ctx)) return null;
        // Captions use the shop's entry indices so they match `buy <i>`.
        return inventory.AllEntries
            .Select((e, i) => (Entry: e, Index: i))
            .Where(x => x.Entry is MerchantCardEntry { CreationResult: not null } && x.Entry.IsStocked)
            .Select(x => new SheetCard(((MerchantCardEntry)x.Entry).CreationResult!.Card, $"{x.Index} · {x.Entry.Cost}g", PileType.None))
            .ToList();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Tools.ToolError(message);
    }
}
