using System;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Held while an agent's `act` runs through this mod. While held, card selections the game asks for go to this mod's
/// agent (<see cref="ChoiceBroker"/>). <see cref="Busy"/> adds the other mod's outside acts (<see cref="Bridge"/>):
/// while busy the in-game chat covers the game so the player's clicks do not mix with an agent's actions. Outside it
/// the player plays as usual. Main thread only.
/// </summary>
internal static class AgentLock
{
    private static int _depth;

    public static bool Held => _depth > 0;

    /// <summary>Held, or an outside client acting through the other mod.</summary>
    public static bool Busy => Held || Bridge.ExternalActing;

    /// <summary>Raised with <see cref="Busy"/> when this lock or an outside act starts or ends.</summary>
    public static event Action<bool>? BusyChanged;

    /// <summary>The chat mod: also raise <see cref="BusyChanged"/> for the other mod's outside acts.</summary>
    public static void WatchExternalActs() => Bridge.OnExternalAct(_ => BusyChanged?.Invoke(Busy));

    public static void Enter()
    {
        if (_depth++ > 0) return;
        ChoiceBroker.Instance.Refresh();
        BusyChanged?.Invoke(Busy);
    }

    public static void Exit()
    {
        if (_depth == 0 || --_depth > 0) return;
        ChoiceBroker.Instance.Refresh();
        BusyChanged?.Invoke(Busy);
    }
}
