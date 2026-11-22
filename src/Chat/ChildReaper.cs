using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Outside Windows (no job objects): remembers the children the game started and ends their process trees when the
/// game exits normally. A killed game leaves them running; Claude Code still ends when its input pipe closes.
/// </summary>
internal static class ChildReaper
{
    private static readonly List<Process> Children = new();
    private static bool _hooked;

    public static void Track(Process process)
    {
        lock (Children)
        {
            Children.RemoveAll(HasExited);
            Children.Add(process);
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
        }
    }

    private static void KillAll()
    {
        Process[] children;
        lock (Children)
        {
            children = Children.ToArray();
            Children.Clear();
        }
        foreach (var child in children)
        {
            if (HasExited(child)) continue;
            try { child.Kill(entireProcessTree: true); }
            catch (Exception ex) { Log.Warn($"chat: could not end child {SafeId(child)}: {ex.Message}"); }
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; } // disposed or never started
    }

    private static string SafeId(Process process)
    {
        try { return process.Id.ToString(); }
        catch { return "?"; }
    }
}
