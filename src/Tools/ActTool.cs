using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Session;

namespace Sts2AgentMcp.Tools;

internal sealed class ActTool : ITool
{
    private readonly ActRunner _runner;

    public ActTool(ActRunner runner) => _runner = runner;

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

    public Task<string> Call(JsonObject args)
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
        return _runner.Run(actions);
    }
}
