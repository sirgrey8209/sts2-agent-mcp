using System;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Held while an agent's `act` runs (any MCP client). While held, card selections the game asks for go to the agent
/// (<see cref="ChoiceBroker"/>), and the in-game chat covers the game so the player's clicks do not mix with the
/// agent's actions. Outside it the player plays as usual. Main thread only.
/// </summary>
internal static class AgentLock
{
    private static int _depth;

    public static bool Held => _depth > 0;

    /// <summary>Raised when the lock is taken (true) or released (false).</summary>
    public static event Action<bool>? Changed;

    public static void Enter()
    {
        if (_depth++ > 0) return;
        ChoiceBroker.Instance.Refresh();
        Changed?.Invoke(true);
    }

    public static void Exit()
    {
        if (_depth == 0 || --_depth > 0) return;
        ChoiceBroker.Instance.Refresh();
        Changed?.Invoke(false);
    }
}
