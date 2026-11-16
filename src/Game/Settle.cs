using System;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

internal enum SettleMode
{
    /// <summary>Combat action: wait until the player can act again (or the screen changes / combat ends).</summary>
    Combat,
    /// <summary>End turn: wait until the next player turn starts (or combat ends).</summary>
    TurnEnd,
    /// <summary>Screen action that may or may not change the screen: wait until things stop changing.</summary>
    Screen,
    /// <summary>Screen action that must change the screen: wait for a different screen, then stability.</summary>
    ScreenChange,
}

internal sealed record SettleResult(string Outcome, Observation Final, string? Diagnostic = null)
{
    public bool TimedOut => Outcome == "timeout";
}

/// <summary>
/// Decides when the game has settled after an action. Combat readiness alone is not enough, because the turn end and
/// enemy turn run outside the action queue; screen stability is measured over several frames.
/// </summary>
internal static class Settle
{
    private const int CombatStableFrames = 2;
    private const int ScreenStableFrames = 3;
    private const int MinFrames = 2;
    /// <summary>After combat ends the room lingers before the rewards/map screen appears.</summary>
    private static readonly TimeSpan PostCombatGrace = TimeSpan.FromSeconds(10);

    public static TimeSpan DefaultTimeout(SettleMode mode) => mode switch
    {
        SettleMode.TurnEnd => TimeSpan.FromSeconds(30),
        SettleMode.ScreenChange => TimeSpan.FromSeconds(20),
        _ => TimeSpan.FromSeconds(15),
    };

    public static Task<SettleResult> Wait(SettleMode mode, Observation before, TimeSpan? timeout = null, Func<bool>? until = null)
    {
        int frames = 0;
        int stable = 0;
        Observation? last = null;
        bool screenChanged = false;
        DateTime? combatEndedAt = null;

        SettleResult? Step()
        {
            var now = Observation.Capture();
            frames++;
            bool sameAsLast = last != null && last.ScreenKey == now.ScreenKey;
            last = now;

            if (now.ChoicePending) return new SettleResult("choice", now);
            if (frames < MinFrames) return null;
            if (until != null && now.ScreenKey == before.ScreenKey)
            {
                bool met;
                try { met = until(); } catch { met = true; }
                if (!met)
                {
                    stable = 0;
                    return null;
                }
            }

            bool screenStable = now.Idle && !now.Transitioning && sameAsLast;
            if (now.ScreenKey != before.ScreenKey) screenChanged = true;

            // Combat just ended but the next screen (rewards, map, game over) has not appeared yet.
            if (before.InCombat && !now.InCombat && now.CombatRoomScreen)
            {
                combatEndedAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - combatEndedAt < PostCombatGrace)
                {
                    stable = 0;
                    return null;
                }
            }

            switch (mode)
            {
                case SettleMode.Combat:
                case SettleMode.TurnEnd:
                    if (!now.InCombat || now.ScreenKey != before.ScreenKey)
                    {
                        stable = screenStable ? stable + 1 : 0;
                        if (stable >= ScreenStableFrames)
                            return new SettleResult(now.InCombat ? "screen_changed" : "combat_ended", now);
                        return null;
                    }
                    bool ready = now.CombatReady && !now.Transitioning
                                 && (mode == SettleMode.Combat || now.PlayerTurn > before.PlayerTurn);
                    stable = ready ? stable + 1 : 0;
                    return stable >= CombatStableFrames ? new SettleResult("ready", now) : null;

                case SettleMode.ScreenChange:
                    if (!screenChanged) return null;
                    stable = screenStable ? stable + 1 : 0;
                    if (stable < ScreenStableFrames) return null;
                    if (AwaitingPlayerTurn(now)) return null;
                    return new SettleResult("screen_changed", now);

                default:
                    stable = screenStable ? stable + 1 : 0;
                    if (stable < ScreenStableFrames) return null;
                    if (AwaitingPlayerTurn(now)) return null;
                    return new SettleResult(screenChanged ? "screen_changed" : "ready", now);
            }
        }

        // A screen action that lands in a combat room should wait for the player's turn. Only on the combat screen:
        // after death the game-over overlay shows while combat is still formally in progress.
        static bool AwaitingPlayerTurn(Observation now) => now.InCombat && now.CombatRoomScreen && !now.CombatReady;

        SettleResult OnTimeout()
        {
            var now = last ?? before;
            return new SettleResult("timeout", now,
                $"screen={now.ScreenKey} inCombat={now.InCombat} combatReady={now.CombatReady} idle={now.Idle} transition={now.Transitioning} frames={frames}");
        }

        return MainThread.Poll(Step, timeout ?? DefaultTimeout(mode), OnTimeout);
    }
}
