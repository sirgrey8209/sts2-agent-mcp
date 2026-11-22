using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2AgentMcp.Game;

/// <summary>Snapshot of the commonly needed game objects. Capture on the main thread only.</summary>
internal sealed class GameCtx
{
    public IScreenContext? Screen { get; private init; }
    public RunState? Run { get; private init; }
    public Player? Me { get; private init; }
    public CombatState? Combat { get; private init; }
    public bool InCombat { get; private init; }

    public static GameCtx Capture()
    {
        var run = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        var combatManager = CombatManager.Instance;
        bool inCombat = combatManager is { IsInProgress: true };
        return new GameCtx
        {
            Screen = ActiveScreenContext.Instance.GetCurrentScreen(),
            Run = run,
            Me = run != null ? LocalContext.GetMe(run) : null,
            Combat = inCombat ? combatManager!.DebugOnlyGetState() : null,
            InCombat = inCombat,
        };
    }
}
