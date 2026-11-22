using System;
using System.Collections.Generic;
using System.Linq;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>A parsed action: "play c3 @e1", "choose 0 2", "new_run IRONCLAD asc=5".</summary>
internal sealed class ActionSpec
{
    private ActionSpec(string raw, string verb, List<string> args, Dictionary<string, string> options, string? target)
    {
        Raw = raw;
        Verb = verb;
        Args = args;
        Options = options;
        Target = target;
    }

    public string Raw { get; }
    public string Verb { get; }
    public List<string> Args { get; }
    public Dictionary<string, string> Options { get; }
    /// <summary>The "@xxx" token without '@', if any.</summary>
    public string? Target { get; }

    public static ActionSpec Parse(string raw)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string verb = tokens.Length > 0 ? tokens[0].ToLowerInvariant() : "";
        var args = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? target = null;
        foreach (var token in tokens.Skip(1))
        {
            if (token.StartsWith('@') && token.Length > 1) target = token[1..];
            else if (token.Contains('=') && token.IndexOf('=') > 0)
            {
                int eq = token.IndexOf('=');
                options[token[..eq]] = token[(eq + 1)..];
            }
            else args.Add(token);
        }
        return new ActionSpec(raw, verb, args, options, target);
    }

    public string? Arg(int index) => index < Args.Count ? Args[index] : null;

    public int? IntArg(int index) => int.TryParse(Arg(index), out int v) ? v : null;

    public List<int>? IntArgs()
    {
        var result = new List<int>();
        foreach (var a in Args)
        {
            if (!int.TryParse(a, out int v)) return null;
            result.Add(v);
        }
        return result;
    }
}

internal sealed class ExecResult
{
    public string? Error { get; private init; }
    public SettleMode Settle { get; private init; }
    public TimeSpan? Timeout { get; private init; }
    public string? Note { get; private init; }
    /// <summary>Actions to run next (macro expansion), each after the previous one settles.</summary>
    public List<string> Followups { get; private init; } = new();
    /// <summary>
    /// Extra completion condition checked every frame on the main thread (e.g. "the rest site's proceed button is enabled"),
    /// for screens that animate without a transition or action-queue activity.
    /// </summary>
    public Func<bool>? Until { get; private init; }

    public static ExecResult Fail(string error) => new() { Error = error };

    public static ExecResult Ok(SettleMode settle, string? note = null, TimeSpan? timeout = null, IEnumerable<string>? followups = null,
        Func<bool>? until = null) =>
        new() { Settle = settle, Note = note, Timeout = timeout, Followups = followups?.ToList() ?? new List<string>(), Until = until };
}

internal sealed class ScreenView
{
    public ScreenView(string kind) => Kind = kind;

    public string Kind { get; }
    public List<string> Lines { get; } = new();
    public string Actions { get; set; } = "";

    public ScreenView Add(string line)
    {
        Lines.Add(line);
        return this;
    }

    public ScreenView AddRange(IEnumerable<string> lines, string indent = "")
    {
        foreach (var line in lines) Lines.Add(indent + line);
        return this;
    }
}

internal interface IScreenHandler
{
    string Kind { get; }
    bool Matches(GameCtx ctx);
    ScreenView Render(GameCtx ctx, bool full);
    /// <summary>Starts the action on the main thread; the runner then waits per the returned settle mode.</summary>
    ExecResult Execute(GameCtx ctx, ActionSpec action);
}

internal static class ScreenRegistry
{
    // Order matters: first match wins. Generic is the fallback that keeps unknown screens playable.
    private static readonly IScreenHandler[] Handlers =
    {
        new ChoiceHandler(),
        new PopupHandler(),
        new LobbyHandler(),
        new TimelineHandler(),
        new MainMenuHandler(),
        new GameOverHandler(),
        new CrystalSphereHandler(),
        new BundleHandler(),
        new CardRewardHandler(),
        new RewardsHandler(),
        new MapHandler(),
        new ShopHandler(),
        new EventRoomHandler(),
        new RestSiteHandler(),
        new TreasureHandler(),
        new CombatHandler(),
        new GenericHandler(),
    };

    public static IScreenHandler Find(GameCtx ctx)
    {
        foreach (var handler in Handlers)
        {
            if (!Host.Features.IsEnabled("screen:" + handler.Kind)) continue;
            try
            {
                if (handler.Matches(ctx)) return handler;
            }
            catch (Exception ex) when (Host.Features.IsBindingFailure(ex))
            {
                Host.Features.Disable("screen:" + handler.Kind, ex);
            }
            catch (Exception ex)
            {
                Host.Log.Warn($"{handler.Kind}.Matches failed: {ex.Message}");
            }
        }
        return Handlers[^1];
    }
}
