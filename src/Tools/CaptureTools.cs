using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Screens;

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
        "the index for `choose <i>`) and return its path (to forward to the user).";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["attach"] = CaptureArgs.AttachSchema(),
            ["max_width"] = new JsonObject { ["type"] = "integer", ["description"] = "Downscale to this width (default: no limit)." },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var snapshot = await MainThread.Run(() =>
        {
            var run = GameCtx.Capture().Run ?? throw new ToolError("no run in progress");
            return (Run: run, Next: MapHandler.NextCoords(run));
        });
        var capture = await MapRender.Render(snapshot.Run, snapshot.Next, CaptureArgs.Int(args, "max_width", 0));
        return CaptureArgs.Result(capture, CaptureArgs.Attach(args), "map");
    }
}

/// <summary>Cards drawn with the game's own card visuals into one PNG sheet.</summary>
internal sealed class RenderCardsTool : ITool
{
    public string Name => "render_cards";

    public string Description =>
        "Render cards with the game's card art into one PNG sheet and return its path (to forward to the user). " +
        $"Pick cards with `source` ({CardSources.Help}) or `cards` (combat ids like c3, or card ids/names like BASH). " +
        "Captions show the id or index to use with `act`.";

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

        var cards = await MainThread.Run(() =>
            ids.Count > 0 ? CardSources.FromIds(ids) : CardSources.FromSource(GameCtx.Capture(), source!));
        if (cards.Count == 0) return "no cards to render";

        var capture = await Capture.RenderCards(cards, CaptureArgs.Int(args, "columns", 5), CaptureArgs.Int(args, "max_width", 1600));
        return CaptureArgs.Result(capture, CaptureArgs.Attach(args), $"{cards.Count} card(s)");
    }
}
