using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Tools;

namespace Sts2AgentMcp.Host;

/// <summary>
/// Minimal MCP server over Streamable HTTP: one JSON-RPC request in, one JSON response out (no SSE).
/// </summary>
internal sealed class McpEndpoint
{
    public const string ServerVersion = "0.1.0";
    private static readonly string[] SupportedVersions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

    private readonly ToolRegistry _tools;

    public McpEndpoint(ToolRegistry tools) => _tools = tools;

    /// <summary>Returns the JSON response body, or null for notifications (HTTP 202).</summary>
    public async Task<string?> Handle(string body)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (JsonException) { return Error(null, -32700, "parse error"); }

        if (node is not JsonObject request)
            return Error(null, -32600, "expected a single JSON-RPC request object");

        JsonNode? id = request["id"]?.DeepClone();
        string? method = (request["method"] as JsonValue)?.GetValue<string>();
        var parameters = request["params"] as JsonObject;

        if (method == null) return id == null ? null : Error(id, -32600, "missing method");
        if (id == null) return null; // notification (e.g. notifications/initialized)

        try
        {
            switch (method)
            {
                case "initialize":
                    return Result(id, Initialize(parameters));
                case "ping":
                    return Result(id, new JsonObject());
                case "tools/list":
                    return Result(id, new JsonObject { ["tools"] = _tools.Describe() });
                case "tools/call":
                    return Result(id, await CallTool(parameters));
                default:
                    return Error(id, -32601, $"method not found: {method}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"{method} failed: {ex}");
            return Error(id, -32603, "internal error: " + ex.Message);
        }
    }

    private static JsonObject Initialize(JsonObject? parameters)
    {
        string? requested = (parameters?["protocolVersion"] as JsonValue)?.GetValue<string>();
        string version = requested != null && SupportedVersions.Contains(requested) ? requested : SupportedVersions[0];
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "sts2", ["version"] = ServerVersion },
            ["instructions"] =
                "Slay the Spire 2 (single-player). Call `state` to see the current screen and the exact actions it accepts, " +
                "then `act` with one or more of those actions. `act` waits until the game settles and returns what happened plus the new screen. " +
                "Use `info` for card/relic/power/potion/monster descriptions.",
        };
    }

    private async Task<JsonObject> CallTool(JsonObject? parameters)
    {
        string? name = (parameters?["name"] as JsonValue)?.GetValue<string>();
        var args = parameters?["arguments"] as JsonObject ?? new JsonObject();
        var (output, isError) = await _tools.Call(name ?? "", args);
        var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = output.Text });
        foreach (var image in output.Images)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(image.Data),
                ["mimeType"] = image.MimeType,
            });
        }
        return new JsonObject { ["content"] = content, ["isError"] = isError };
    }

    private static string Result(JsonNode id, JsonNode result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();
}
