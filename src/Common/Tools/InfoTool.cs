using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Tools;

internal sealed class InfoTool : ITool
{
    public string Name => "info";

    public string Description =>
        "Look up cards, relics, potions, powers, characters, monsters, encounters and events by id (e.g. CARD.BASH, BASH) " +
        "or by the name shown on screen. Cards include their upgraded version. To find items by text or filters " +
        "(e.g. all 0-cost Defect cards), use `search`. Monsters and encounters include their move pattern (moves, base damage for this " +
        "run's ascension, follow-ups, random weights). Special ids: \"boss\" = this act's boss; \"e1\" (an enemy in the " +
        "current fight) = its move history, announced move and what follows.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["ids"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
            },
        },
        ["required"] = new JsonArray("ids"),
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var ids = new List<string>();
        if (args["ids"] is JsonArray array)
        {
            foreach (var item in array)
                if (item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
                    ids.Add(s.Trim());
        }
        if (ids.Count == 0) throw new ToolError("`ids` must be a non-empty array of strings");
        if (!Features.IsEnabled("catalog")) throw new ToolError("info is unavailable: " + Features.Summary());
        return await MainThread.Run(() => Catalog.Describe(ids));
    }
}
