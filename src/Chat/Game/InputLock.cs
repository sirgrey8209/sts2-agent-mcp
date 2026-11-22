using System;
using Godot;
using HarmonyLib;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>
/// While an agent acts (<see cref="AgentLock"/>), the game ignores the player's keys and buttons: the game reads its
/// shortcuts and clicks as input actions (InputEvent.IsActionPressed and friends from its hotkey manager, hand, map,
/// screens; Input.IsActionJustPressed when polling), and these answer "not this action" meanwhile. The agent's own
/// actions are unaffected (they press buttons directly, ForceClick, or call the game). Typing in the chat panel is
/// unaffected too (text fields read keys natively, not through these calls). Only with the in-game chat.
/// </summary>
internal static class InputLock
{
    private static bool _installed;

    /// <summary>Main thread. Patches once.</summary>
    public static void Install()
    {
        if (_installed) return;
        Features.Try("input-lock", () =>
        {
            var harmony = new Harmony("sirgrey8209.STS2AgentChat.InputLock");
            var onEvent = new HarmonyMethod(typeof(InputLock), nameof(EventPrefix));
            var onPoll = new HarmonyMethod(typeof(InputLock), nameof(PollPrefix));
            foreach (var name in new[] { nameof(InputEvent.IsActionPressed), nameof(InputEvent.IsActionReleased), nameof(InputEvent.IsAction) })
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(InputEvent)))
                    if (method.Name == name && method.ReturnType == typeof(bool)) harmony.Patch(method, prefix: onEvent);
            foreach (var name in new[] { nameof(Input.IsActionJustPressed), nameof(Input.IsActionPressed), nameof(Input.IsActionJustReleased) })
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(Input)))
                    if (method.Name == name && method.ReturnType == typeof(bool)) harmony.Patch(method, prefix: onPoll);
            _installed = true;
            Log.Info("input lock installed");
        });
    }

    private static bool EventPrefix(ref bool __result)
    {
        if (!AgentLock.Busy) return true;
        __result = false;
        return false;
    }

    private static bool PollPrefix(ref bool __result)
    {
        if (!AgentLock.Busy) return true;
        __result = false;
        return false;
    }
}
