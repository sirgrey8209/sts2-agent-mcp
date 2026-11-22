using MegaCrit.Sts2.Core.Combat;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Remembers the last round whose player turn fully started. The game sets the player's phase to Play before the
/// turn setup finishes (hand draw, start-of-turn hooks); CombatManager.TurnStarted fires only after all of it.
/// </summary>
internal static class TurnTracker
{
    /// <summary>Round number of the last completed player turn start, or -1. Main thread only.</summary>
    public static int PlayerTurnStartedRound { get; private set; } = -1;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        var cm = CombatManager.Instance;
        cm.CombatSetUp += _ => PlayerTurnStartedRound = -1;
        cm.CombatEnded += _ => PlayerTurnStartedRound = -1;
        cm.TurnStarted += state =>
        {
            if (state.CurrentSide == CombatSide.Player) PlayerTurnStartedRound = state.RoundNumber;
        };
        Installed = true;
    }
}
