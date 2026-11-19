using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Screens;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Tools;

internal static class CaptureArgs
{
    public static bool Attach(JsonObject args) => args["attach"] is JsonValue v && v.TryGetValue(out bool b) && b;

    public static int Int(JsonObject args, string name, int fallback) =>
        args[name] is JsonValue v && v.TryGetValue(out int i) && i > 0 ? i : fallback;

    public static JsonObject AttachSchema() => new()
    {
        ["type"] = "boolean",
        ["description"] = "Also return the image to the caller (costs tokens). Default false: only the file path.",
    };

    public static ToolOutput Result(CaptureResult capture, bool attach, string what) =>
        new($"{what}: {capture.Path} ({capture.Width}x{capture.Height})",
            attach ? new[] { new ToolImage(capture.Data, capture.MimeType) } : null);
}

/// <summary>Screenshot of the game window, saved as PNG (for showing the user the current screen).</summary>
internal sealed class ScreenshotTool : ITool
{
    public string Name => "screenshot";

    public string Description =>
        "Save a screenshot of the game window as a JPEG file and return its path (to forward to the user). " +
        "attach=true also returns the image itself.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["attach"] = CaptureArgs.AttachSchema(),
            ["max_width"] = new JsonObject { ["type"] = "integer", ["description"] = "Downscale to this width (default 1600)." },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        int maxWidth = CaptureArgs.Int(args, "max_width", 1600);
        var capture = await MainThread.Run(() => Capture.Screenshot(maxWidth));
        return CaptureArgs.Result(capture, CaptureArgs.Attach(args), "screenshot");
    }
}

/// <summary>The whole act map as one image (game icons, path taken, next choices with their `choose` indices).</summary>
internal sealed class RenderMapTool : ITool
{
    public string Name => "render_map";

    public string Description =>
        "Render the whole act map as one PNG (game map icons, path taken in red, next choices in green labelled with " +
        "the index for `choose <i>`) and return its path (to forward to the user). Optional annotations: node " +
        "coordinates, extra paths (e.g. route options to compare), marked nodes with text, and notes under the map. " +
        "Nodes are written r<row>c<col> as on the map screen (r7c3), or \"current\" / \"boss\". Colors: names (blue, " +
        "orange, purple...) or #rrggbb; omitted colors are picked from a palette.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["coords"] = new JsonObject { ["type"] = "boolean", ["description"] = "Write r<row>c<col> under every node." },
            ["paths"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["nodes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["minItems"] = 2 },
                        ["color"] = new JsonObject { ["type"] = "string" },
                        ["label"] = new JsonObject { ["type"] = "string", ["description"] = "Shown in the legend under the map." },
                    },
                    ["required"] = new JsonArray("nodes"),
                },
            },
            ["marks"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["node"] = new JsonObject { ["type"] = "string" },
                        ["color"] = new JsonObject { ["type"] = "string" },
                        ["text"] = new JsonObject { ["type"] = "string", ["description"] = "Short text drawn next to the node." },
                    },
                    ["required"] = new JsonArray("node"),
                },
            },
            ["notes"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Lines of text under the map.",
            },
            ["attach"] = CaptureArgs.AttachSchema(),
            ["max_width"] = new JsonObject { ["type"] = "integer", ["description"] = "Downscale to this width (default: no limit)." },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var notes = new MapRender.Annotations(
            args["coords"] is JsonValue c && c.TryGetValue(out bool coords) && coords,
            Objects(args["paths"]).Select(p => new MapRender.PathSpec(Strings(p["nodes"]), Str(p["color"]), Str(p["label"]))).ToList(),
            Objects(args["marks"]).Select(m => new MapRender.MarkSpec(
                Str(m["node"]) ?? throw new ToolError("each mark needs `node`"), Str(m["color"]), Str(m["text"]))).ToList(),
            Strings(args["notes"]));
        var snapshot = await MainThread.Run(() =>
        {
            var run = GameCtx.Capture().Run ?? throw new ToolError("no run in progress");
            return (Run: run, Next: MapHandler.NextCoords(run));
        });
        var capture = await MapRender.Render(snapshot.Run, snapshot.Next, CaptureArgs.Int(args, "max_width", 0), notes);
        return CaptureArgs.Result(capture, CaptureArgs.Attach(args), "map");
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonObject>() : Enumerable.Empty<JsonObject>();

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(Str).Where(s => s != null).Select(s => s!).ToList()
            : new List<string>();

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
}

/// <summary>Cards drawn with the game's own card visuals into one PNG sheet.</summary>
internal sealed class RenderCardsTool : ITool
{
    public string Name => "render_cards";

    public string Description =>
        "Render cards with the game's card art into one PNG sheet and return its path (to forward to the user). " +
        $"Pick cards with `source` ({CardSources.Help}) or `cards` (combat ids like c3, or card ids/names like BASH). " +
        "Captions show the id or index to use with `act`. The result also lists the card names as text " +
        "(duplicates as xN; the draw pile sorted), so the image is only needed to see the cards.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["source"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("hand", "draw", "discard", "exhaust", "deck", "choice", "screen"),
            },
            ["cards"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["columns"] = new JsonObject { ["type"] = "integer", ["description"] = "Cards per row (default 5)." },
            ["attach"] = CaptureArgs.AttachSchema(),
            ["max_width"] = new JsonObject { ["type"] = "integer", ["description"] = "Downscale to this width (default 1600)." },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        string? source = (args["source"] as JsonValue)?.GetValue<string>();
        var ids = new List<string>();
        if (args["cards"] is JsonArray array)
        {
            foreach (var item in array)
                if (item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
                    ids.Add(s.Trim());
        }
        if (source == null && ids.Count == 0) throw new ToolError("give `source` or `cards`");

        var (cards, names) = await MainThread.Run(() =>
        {
            var list = ids.Count > 0 ? CardSources.FromIds(ids) : CardSources.FromSource(GameCtx.Capture(), source!);
            return (list, NameList(list, sorted: source == "draw"));
        });
        if (cards.Count == 0) return "no cards to render";

        var capture = await Capture.RenderCards(cards, CaptureArgs.Int(args, "columns", 5), CaptureArgs.Int(args, "max_width", 1600));
        var result = CaptureArgs.Result(capture, CaptureArgs.Attach(args), $"{cards.Count} card(s)");
        return new ToolOutput(result.Text + "\ncards: " + names, result.Images);
    }

    /// <summary>
    /// The cards as text (same labels as `state detail=full`), duplicates as "xN", so the sheet need not be read for
    /// names alone. The draw pile is sorted (its order is hidden in the game); other sources keep their order.
    /// </summary>
    private static string NameList(List<SheetCard> cards, bool sorted)
    {
        var groups = cards.Select(c => Fmt.CardLabel(c.Card)).GroupBy(n => n);
        if (sorted) groups = groups.OrderBy(g => g.Key);
        return string.Join(", ", groups.Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
    }
}
