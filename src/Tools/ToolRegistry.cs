using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Tools;

internal interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonObject InputSchema();
    Task<ToolOutput> Call(JsonObject args);
}

/// <summary>An image returned to the MCP client (PNG bytes).</summary>
internal sealed record ToolImage(byte[] Data, string MimeType = "image/png");

/// <summary>Tool result: text plus optional images.</summary>
internal sealed class ToolOutput
{
    public ToolOutput(string text, IEnumerable<ToolImage>? images = null)
    {
        Text = text;
        Images = images?.ToList() ?? new List<ToolImage>();
    }

    public string Text { get; }
    public List<ToolImage> Images { get; }

    public static implicit operator ToolOutput(string text) => new(text);
}

/// <summary>Thrown by tools for user-facing errors (returned with isError=true, no stack trace).</summary>
internal sealed class ToolError : Exception
{
    public ToolError(string message) : base(message) { }
}

internal sealed class ToolRegistry
{
    private readonly List<ITool> _tools = new();
    // Game interaction is inherently sequential; serialize tool calls (shared with extended registries).
    private readonly SemaphoreSlim _gate;

    public ToolRegistry() : this(new SemaphoreSlim(1, 1)) { }

    private ToolRegistry(SemaphoreSlim gate) => _gate = gate;

    public ToolRegistry Add(ITool tool)
    {
        _tools.Add(tool);
        return this;
    }

    /// <summary>
    /// A registry with this one's tools plus <paramref name="extra"/>, sharing the call gate (so calls through either
    /// still run one at a time). Used for the in-game chat endpoint; this registry is left as it is.
    /// </summary>
    public ToolRegistry Extend(IEnumerable<ITool> extra)
    {
        var result = new ToolRegistry(_gate);
        result._tools.AddRange(_tools);
        result._tools.AddRange(extra);
        return result;
    }

    /// <summary>Puts <paramref name="tool"/> in place of the tool with the same name (this registry only).</summary>
    public ToolRegistry Replace(ITool tool)
    {
        int i = _tools.FindIndex(t => t.Name == tool.Name);
        if (i >= 0) _tools[i] = tool;
        else _tools.Add(tool);
        return this;
    }

    public JsonArray Describe() =>
        new(_tools.Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["inputSchema"] = t.InputSchema(),
        }).ToArray());

    public async Task<(ToolOutput Output, bool IsError)> Call(string name, JsonObject args)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == name);
        if (tool == null) return ($"unknown tool: {name}", true);

        await _gate.WaitAsync();
        try
        {
            return (await tool.Call(args), false);
        }
        catch (ToolError ex)
        {
            return (ex.Message, true);
        }
        catch (Exception ex)
        {
            Log.Error($"tool {name} failed: {ex}");
            return ($"internal error in {name}: {ex.GetType().Name}: {ex.Message}", true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
