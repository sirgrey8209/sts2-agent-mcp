using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sts2AgentMcp.Host;

/// <summary>
/// What the two mods (STS2AgentMcp, STS2AgentChat) share when both run in the game. They compile the same source but
/// are separate assemblies, so their static fields are separate; this keeps the shared state in one process-wide slot
/// (AppDomain data) holding only base-library types, which both see as the same objects.
/// <list type="bullet">
/// <item>The tool-call gate: calls through either mod run one at a time (one game).</item>
/// <item>External acts: an outside MCP client acting through STS2AgentMcp. The chat steps back for it (the external
/// client comes first): its agents stop, the game is covered and the player's input is locked meanwhile.</item>
/// <item>Chat present: STS2AgentMcp then takes card selections only while its own act runs (the player picks on the
/// game's screens otherwise), as it did with the chat panel on.</item>
/// <item>The game's rect in the window (the chat panel takes the left side): screenshots crop to it.</item>
/// </list>
/// </summary>
internal static class Bridge
{
    private const string Key = "sirgrey8209.Sts2Agent.Bridge.v1";

    private static readonly Dictionary<string, object> Slots = Resolve();

    private static Dictionary<string, object> Resolve()
    {
        // typeof(AppDomain) is the same object for both mods (base library), so it serves as their common lock.
        lock (typeof(AppDomain))
        {
            if (AppDomain.CurrentDomain.GetData(Key) is Dictionary<string, object> existing) return existing;
            var created = new Dictionary<string, object>();
            AppDomain.CurrentDomain.SetData(Key, created);
            return created;
        }
    }

    private static T Slot<T>(string name, Func<T> create) where T : class
    {
        lock (Slots)
        {
            if (Slots.TryGetValue(name, out var value) && value is T typed) return typed;
            var made = create();
            Slots[name] = made;
            return made;
        }
    }

    /// <summary>Serializes tool calls of both mods.</summary>
    public static SemaphoreSlim Gate => Slot("gate", () => new SemaphoreSlim(1, 1));

    private static StrongBox<int> ExternalActs => Slot("externalActs", () => new StrongBox<int>(0));
    private static List<Action<bool>> ExternalListeners => Slot("externalListeners", () => new List<Action<bool>>());

    /// <summary>An outside client's act is running now.</summary>
    public static bool ExternalActing
    {
        get { lock (Slots) return ExternalActs.Value > 0; }
    }

    /// <summary>Main thread. An outside client's act starts: listeners get true.</summary>
    public static void ExternalActBegin() => ChangeExternal(+1);

    /// <summary>Main thread. An outside client's act ended: listeners get false.</summary>
    public static void ExternalActEnd() => ChangeExternal(-1);

    /// <summary>Called on the main thread with true when an outside act starts and false when it ends.</summary>
    public static void OnExternalAct(Action<bool> listener)
    {
        lock (Slots) ExternalListeners.Add(listener);
    }

    private static void ChangeExternal(int delta)
    {
        Action<bool>[] listeners;
        lock (Slots)
        {
            var box = ExternalActs;
            if (delta < 0 && box.Value == 0) return;
            box.Value += delta;
            listeners = ExternalListeners.ToArray();
        }
        foreach (var listener in listeners)
        {
            try { listener(delta > 0); }
            catch (Exception ex) { Log.Error($"external act listener failed: {ex}"); }
        }
    }

    private static StrongBox<bool> ChatFlag => Slot("chatPresent", () => new StrongBox<bool>(false));

    /// <summary>The in-game chat mod is loaded.</summary>
    public static bool ChatPresent
    {
        get { lock (Slots) return ChatFlag.Value; }
        set { lock (Slots) ChatFlag.Value = value; }
    }

    private static StrongBox<Func<int[]?>?> GameRectSource => Slot("gameRect", () => new StrongBox<Func<int[]?>?>(null));

    /// <summary>The chat mod: where the game is drawn, as {x, y, width, height} in window pixels (null: whole window).</summary>
    public static void SetGameRect(Func<int[]?> source)
    {
        lock (Slots) GameRectSource.Value = source;
    }

    /// <summary>Main thread. The game's rect in window pixels, or null when the game has the whole window.</summary>
    public static int[]? GameRect()
    {
        Func<int[]?>? source;
        lock (Slots) source = GameRectSource.Value;
        try { return source?.Invoke(); }
        catch (Exception ex)
        {
            Log.Warn($"game rect unavailable: {ex.Message}");
            return null;
        }
    }
}
