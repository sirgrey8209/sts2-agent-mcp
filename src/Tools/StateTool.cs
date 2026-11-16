using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Session;

namespace Sts2AgentMcp.Tools;

internal sealed class StateTool : ITool
{
    private readonly ActRunner _runner;

    public StateTool(ActRunner runner) => _runner = runner;

    public string Name => "state";

    public string Description =>
        "Current screen of Slay the Spire 2: run status, screen contents, and the exact actions accepted by `act`. " +
        "detail=full adds deck, pile contents and relic descriptions.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["detail"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("brief", "full"),
                ["description"] = "brief (default) or full",
            },
        },
    };

    public Task<string> Call(JsonObject args)
    {
        bool full = (args["detail"] as JsonValue)?.GetValue<string>() == "full";
        return _runner.RenderCurrent(full);
    }
}
