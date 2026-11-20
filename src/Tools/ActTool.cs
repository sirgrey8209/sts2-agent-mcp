using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Session;

namespace Sts2AgentMcp.Tools;

/// <summary>An MCP client other than the in-game chat acting on the game (the in-game chat stops for it). Main thread.</summary>
internal static class ExternalPlay
{
    private static int _running;

    /// <summary>An outside client's act is running now.</summary>
    public static bool Running => _running > 0;

    /// <summary>Raised on the main thread when an outside client's act starts.</summary>
    public static event Action? Started;

    public static void Begin()
    {
        _running++;
        try { Started?.Invoke(); }
        catch (Exception ex) { Host.Log.Error($"external play handler failed: {ex}"); }
    }

    public static void End()
    {
        if (_running > 0) _running--;
    }
}

internal sealed class ActTool : ITool
{
    private readonly ActRunner _runner;
    private readonly Func<string?>? _guard;
    private readonly bool _external;

    /// <param name="external">An endpoint for outside clients: their acts are noted (<see cref="ExternalPlay"/>).</param>
    /// <param name="guard">Checked before acting (main thread); a returned reason refuses the act.</param>
    public ActTool(ActRunner runner, bool external = true, Func<string?>? guard = null)
    {
        _runner = runner;
        _external = external;
        _guard = guard;
    }

    public string Name => "act";

    public string Description =>
        "Perform one or more actions in order, using the action syntax listed by the current screen " +
        "(e.g. [\"play c3 @e1\", \"play c7\", \"end\"]). After each action the game is allowed to settle. " +
        "Execution stops early on an error, a new choice prompt, a screen change, or combat end. " +
        "Returns what happened and the resulting screen.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["actions"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
                ["description"] = "Actions to perform in order.",
            },
        },
        ["required"] = new JsonArray("actions"),
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var actions = new List<string>();
        if (args["actions"] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
                    actions.Add(s.Trim());
            }
        }
        else if (args["actions"] is JsonValue single && single.TryGetValue(out string? one) && !string.IsNullOrWhiteSpace(one))
        {
            actions.Add(one.Trim());
        }

        if (actions.Count == 0) throw new ToolError("`actions` must be a non-empty array of strings");
        if (_guard != null && await Host.MainThread.Run(_guard) is { } refused) throw new ToolError(refused);
        if (!_external) return await _runner.Run(actions);
        await Host.MainThread.Run(ExternalPlay.Begin);
        try
        {
            return await _runner.Run(actions);
        }
        finally
        {
            await Host.MainThread.Run(ExternalPlay.End);
        }
    }
}
