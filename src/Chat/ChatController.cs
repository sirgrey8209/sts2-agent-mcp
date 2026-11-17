using System.Linq;
using System.Text.Json.Nodes;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>Connects the chat panel to the Claude Code session: player messages out, agent events in (main thread).</summary>
internal static class ChatController
{
    private static AgentSession? _session;
    private static ChatPanel? _panel;

    public static void Attach(ChatPanel panel, Config config)
    {
        if (_panel == panel) return;
        _panel = panel;
        _session ??= new AgentSession(config);
        _session.Event += ev => MainThread.Run(() => Show(ev));
        panel.Submitted += text =>
        {
            panel.AddUser(text);
            _session.Send(text);
        };
    }

    private static void Show(AgentEvent ev)
    {
        var panel = _panel;
        if (panel == null) return;
        switch (ev)
        {
            case AgentEvent.Status s: panel.SetStatus(s.Message); break;
            case AgentEvent.Text t: panel.AddAgent(t.Content); break;
            case AgentEvent.ToolUse u: panel.AddNote(Describe(u.Name, u.Input)); break;
            case AgentEvent.Note n: panel.AddNote(n.Content, n.Error); break;
            case AgentEvent.Busy b: panel.ShowTyping(b.On); break;
        }
    }

    /// <summary>One short line per tool call (the game font has no emoji, so plain marks only).</summary>
    private static string Describe(string name, JsonObject? input)
    {
        string tool = name.StartsWith("mcp__sts2__") ? name["mcp__sts2__".Length..] : name;
        string List(string key) => input?[key] is JsonArray a ? string.Join(", ", a.Select(x => x?.ToString())) : "";
        string text = tool switch
        {
            "state" => "상황 확인",
            "act" => "행동: " + List("actions"),
            "info" => "정보 조회: " + List("ids"),
            "search" => "검색: " + string.Join(" ", new[] { "query", "kind", "pool", "type", "rarity" }
                .Select(k => input?[k]?.ToString()).Where(v => !string.IsNullOrEmpty(v))),
            _ => tool,
        };
        if (text.Length > 120) text = text[..120] + "…";
        return "· " + text + " ·";
    }
}
