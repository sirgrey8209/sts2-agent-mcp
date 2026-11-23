using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Session;

namespace Sts2AgentMcp.Tools;

/// <summary>
/// An outside MCP client (STS2AgentMcp's endpoint) acting on the game; the in-game chat (STS2AgentChat) stops for it.
/// Kept in <see cref="Host.Bridge"/> so the chat mod sees the other mod's acts. Main thread.
/// </summary>
internal static class ExternalPlay
{
    /// <summary>An outside client's act is running now.</summary>
    public static bool Running => Host.Bridge.ExternalActing;

    /// <summary>Raised on the main thread when an outside client's act starts.</summary>
    public static event Action? Started
    {
        add { if (value != null) Host.Bridge.OnExternalAct(begun => { if (begun) value(); }); }
        remove { /* listeners live as long as the game */ }
    }

    public static void Begin() => Host.Bridge.ExternalActBegin();

    public static void End() => Host.Bridge.ExternalActEnd();
}

internal sealed class ActTool : ITool
{
    private readonly ActRunner _runner;
    private readonly Func<string?>? _guard;
    private readonly bool _external;

    /// <param name="external">An endpoint for outside clients: their acts are noted (<see cref="ExternalPlay"/>).</param>
    /// <param name="guard">Checked before acting (main thread); a returned reason refuses the act.</param>
    /// <param name="name">The tool's name (the in-game chat calls it <c>operate</c>).</param>
    public ActTool(ActRunner runner, bool external = true, Func<string?>? guard = null, string name = "act")
    {
        _runner = runner;
        _external = external;
        _guard = guard;
        Name = name;
    }

    public string Name { get; }

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
