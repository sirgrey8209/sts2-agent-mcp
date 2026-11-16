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
    Task<string> Call(JsonObject args);
}

/// <summary>Thrown by tools for user-facing errors (returned with isError=true, no stack trace).</summary>
internal sealed class ToolError : Exception
{
    public ToolError(string message) : base(message) { }
}

internal sealed class ToolRegistry
{
    private readonly List<ITool> _tools = new();
    // Game interaction is inherently sequential; serialize tool calls.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ToolRegistry Add(ITool tool)
    {
        _tools.Add(tool);
        return this;
    }

    public JsonArray Describe() =>
        new(_tools.Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["inputSchema"] = t.InputSchema(),
        }).ToArray());

    public async Task<(string Text, bool IsError)> Call(string name, JsonObject args)
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
