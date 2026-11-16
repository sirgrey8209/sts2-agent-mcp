using System;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>
/// Run-level actions available on any in-run screen (shown with state detail=full):
/// quit_to_menu = the pause menu's "save and quit" (resumes from the last autosave), abandon_run = give up (game over).
/// </summary>
internal static class RunActions
{
    public const string Help = "quit_to_menu · abandon_run";

    public static bool Handles(string verb) => verb is "quit_to_menu" or "abandon_run";

    public static ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        if (ctx.Run == null) return ExecResult.Fail("no run in progress");
        // Tearing down the run while it is still loading or transitioning crashes the game's load task.
        var now = Observation.Capture();
        if (now.Transitioning || !now.Idle)
            return ExecResult.Fail("the game is busy (loading or resolving actions); try again after `wait`");
        switch (action.Verb)
        {
            case "quit_to_menu":
                NRunMusicController.Instance?.StopMusic();
                TaskHelper.RunSafely(NGame.Instance!.ReturnToMainMenu());
                return ExecResult.Ok(SettleMode.ScreenChange, timeout: TimeSpan.FromSeconds(40));
            case "abandon_run":
                RunManager.Instance.Abandon();
                return ExecResult.Ok(SettleMode.ScreenChange, timeout: TimeSpan.FromSeconds(40));
            default:
                return ExecResult.Fail($"unknown run action '{action.Verb}'");
        }
    }
}
