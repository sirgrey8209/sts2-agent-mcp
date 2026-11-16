using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Stable short ids shown to the agent.
/// Cards: c{n} from the game's per-combat card id database (stable while the combat lasts).
/// Creatures: e{n} for enemies, pet{n} for pets, "me" for the local player, all from Creature.CombatId.
/// </summary>
internal static class Ids
{
    public static string Card(CardModel card)
    {
        try
        {
            if (card.IsMutable && NetCombatCardDb.Instance.TryGetCardId(card, out uint id)) return "c" + id;
        }
        catch { /* not a combat card */ }
        return "c?";
    }

    public static CardModel? ResolveCard(string token)
    {
        if (token.Length < 2 || token[0] != 'c' || !uint.TryParse(token[1..], out uint id)) return null;
        return NetCombatCardDb.Instance.TryGetCard(id, out var card) ? card : null;
    }

    public static string Creature(Creature c)
    {
        if (c.IsPet) return "pet" + c.CombatId;
        if (c.Player != null) return "me";
        return "e" + c.CombatId;
    }

    public static Creature? ResolveCreature(CombatState? combat, Creature? me, string token)
    {
        token = token.TrimStart('@');
        if (token == "me") return me;
        if (combat == null) return null;
        string digits = token.StartsWith("pet") ? token[3..] : token.StartsWith("e") ? token[1..] : "";
        if (!uint.TryParse(digits, out uint id)) return null;
        return combat.Creatures.FirstOrDefault(c => c.CombatId == id);
    }
}
