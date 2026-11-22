using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2AgentMcp.Game;

/// <summary>What the settle logic looks at each frame. Captured on the main thread.</summary>
internal sealed record Observation(
    string ScreenKey,
    bool InCombat,
    bool CombatReady,
    int PlayerTurn,
    bool ChoicePending,
    bool Idle,
    bool Transitioning,
    bool CombatRoomScreen)
{
    public static Observation Capture()
    {
        var ctx = GameCtx.Capture();
        var screen = ctx.Screen;
        string key = screen == null ? "none" : $"{screen.GetType().Name}#{RuntimeHelpers.GetHashCode(screen)}";
        if (ctx.InCombat) key += "|combat";

        bool idle = true;
        if (ctx.Run != null)
        {
            var rm = RunManager.Instance;
            idle = !(rm.ActionExecutor?.IsRunning ?? false) && (rm.ActionQueueSet?.IsEmpty ?? true);
        }

        int turn = ctx.Me?.PlayerCombatState?.TurnNumber ?? 0;

        // While a run is being loaded or set up, the active screen context is the old main menu or nothing at all:
        // treat both as a transition. A missing screen is never a settled state.
        bool loadingRun = screen == null || (ctx.Run != null && screen is NMainMenu or NSubmenu);

        return new Observation(
            key,
            ctx.InCombat,
            IsCombatReady(ctx, idle),
            turn,
            ChoiceBroker.Instance.Pending != null,
            idle,
            IsTransitionRunning() || loadingRun,
            screen is NCombatRoom);
    }

    /// <summary>
    /// NTransition.InTransition is cleared 0.2s into the room fade-in while the fade tween (and the game's load task
    /// awaiting it) runs for 0.8s, so the tween itself is checked too.
    /// </summary>
    private static bool IsTransitionRunning()
    {
        var transition = NGame.Instance?.Transition;
        if (transition == null) return false;
        if (transition.InTransition) return true;
        return Ui.GetField(transition, "_tween") is Godot.Tween tween && Godot.GodotObject.IsInstanceValid(tween) && tween.IsRunning();
    }

    /// <summary>The player can act: player's turn, play phase, nothing running or queued.</summary>
    private static bool IsCombatReady(GameCtx ctx, bool idle)
    {
        var cm = CombatManager.Instance;
        if (!ctx.InCombat || cm.IsOverOrEnding || ctx.Combat == null || ctx.Me == null) return false;
        if (ctx.Combat.CurrentSide != CombatSide.Player) return false;
        if (ctx.Me.PlayerCombatState?.Phase != PlayerTurnPhase.Play) return false;
        if (RunManager.Instance.ActionQueueSynchronizer?.CombatState != ActionSynchronizerCombatState.PlayPhase) return false;
        if (cm.PlayerActionsDisabled) return false;
        // Turn setup (hand draw, start-of-turn hooks) must have finished, not just the phase switch.
        if (TurnTracker.Installed && TurnTracker.PlayerTurnStartedRound != ctx.Combat.RoundNumber) return false;
        return idle;
    }
}
