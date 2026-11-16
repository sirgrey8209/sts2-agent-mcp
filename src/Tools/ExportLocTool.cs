using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Localization;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Tools;

/// <summary>
/// Knowledge-base maintenance: dumps localization tables (game language) to JSON so documents can quote the names
/// players see. Not needed for playing.
/// </summary>
internal sealed class ExportLocTool : ITool
{
    public string Name => "export_loc";

    public string Description =>
        "Maintenance (knowledge base): write localization tables (e.g. monsters, events, encounters) as JSON files " +
        "in the capture directory and return their paths. Not needed for playing.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["tables"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Table names (default: monsters, events, encounters).",
            },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var tables = new List<string>();
        if (args["tables"] is JsonArray array)
            foreach (var item in array)
                if (item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)) tables.Add(s.Trim());
        if (tables.Count == 0) tables.AddRange(new[] { "monsters", "events", "encounters" });

        var dumps = await MainThread.Run(() => tables.ToDictionary(t => t, t =>
        {
            var table = LocManager.Instance.GetTable(t);
            return table.Keys.OrderBy(k => k).ToDictionary(k => k, k => table.GetRawText(k));
        }));

        Directory.CreateDirectory(Capture.Directory);
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var lines = new List<string>();
        foreach (var (name, entries) in dumps)
        {
            string path = Path.Combine(Capture.Directory, $"loc-{name}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entries, options));
            lines.Add($"{name}: {path} ({entries.Count} keys)");
        }
        return string.Join("\n", lines);
    }
}
