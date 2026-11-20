using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Models;
using Sts2AgentMcp.Chat;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Screens;

namespace Sts2AgentMcp.Tools;

// Tools of the in-game chat's own sessions (docs/chat-roadmap.md steps 1, 2 and 7). They are served only on the chat
// endpoint (/mcp/chat); clients of /mcp never see them.

internal static class ChatToolArgs
{
    public static List<string> Strings(JsonObject args, string key) =>
        args[key] is JsonArray array
            ? array.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s?.Trim() : null)
                .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList()
            : new List<string>();

    public static string? Str(JsonObject args, string key) =>
        args[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    public static void RequirePanel()
    {
        if (ChatLayout.Panel == null) throw new ToolError("the chat panel is off");
    }
}

/// <summary>Cards in the chat, in the game's card look, with the game's tooltips on hover.</summary>
internal sealed class ShowCardsTool : ITool
{
    public string Name => "show_cards";

    public string Description =>
        "Show cards in the chat panel in the game's card look (the player gets the game's tooltip on hover). " +
        $"Give `cards` (combat ids like c3, or card ids/names like BASH) or `source` ({CardSources.Help}).";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["cards"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["source"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("hand", "draw", "discard", "exhaust", "deck", "choice", "screen"),
            },
        },
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        var ids = ChatToolArgs.Strings(args, "cards");
        string? source = ChatToolArgs.Str(args, "source");
        if (ids.Count == 0 && source == null) throw new ToolError("give `cards` or `source`");
        return MainThread.Run<ToolOutput>(() =>
        {
            ChatToolArgs.RequirePanel();
            var cards = (ids.Count > 0 ? CardSources.FromIds(ids) : CardSources.FromSource(GameCtx.Capture(), source!))
                .Select(c => c.Card).ToList();
            if (cards.Count == 0) return "no cards to show";
            ChatSessions.ShowCards(cards);
            return $"shown {cards.Count} card(s): " + string.Join(", ", cards.Select(View.Fmt.CardLabel));
        });
    }
}

/// <summary>Relics in the chat with their icons, with the game's tooltips on hover.</summary>
internal sealed class ShowRelicsTool : ITool
{
    public string Name => "show_relics";

    public string Description =>
        "Show relics in the chat panel with their game icons (the player gets the relic's tooltip on hover). " +
        "Give `relics` (ids or names), or `mine: true` for all of the player's relics.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["relics"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["mine"] = new JsonObject { ["type"] = "boolean" },
        },
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        var names = ChatToolArgs.Strings(args, "relics");
        bool mine = args["mine"] is JsonValue m && m.TryGetValue(out bool b) && b;
        if (names.Count == 0 && !mine) throw new ToolError("give `relics` or `mine: true`");
        return MainThread.Run<ToolOutput>(() =>
        {
            ChatToolArgs.RequirePanel();
            var owned = GameCtx.Capture().Me?.Relics.ToList() ?? new List<RelicModel>();
            var relics = new List<RelicModel>();
            if (mine) relics.AddRange(owned);
            foreach (var name in names)
                relics.Add(GameViews.FindRelic(owned, name) ?? Catalog.FindRelic(name) ?? throw new ToolError($"relic '{name}' not found"));
            if (relics.Count == 0) return "no relics to show";
            ChatSessions.ShowRelics(relics);
            return $"shown {relics.Count} relic(s): " + string.Join(", ", relics.Select(r => Text.Of(() => r.Title)));
        });
    }
}

/// <summary>Opens one of the game's views on the game screen.</summary>
internal sealed class OpenViewTool : ITool
{
    public string Name => "open_view";

    public string Description =>
        $"Open a view on the game screen to show the player what you are explaining: {GameViews.Help}. " +
        "Only opens the view (nothing is played or chosen); the player closes it.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["view"] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray("view"),
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        string view = ChatToolArgs.Str(args, "view") ?? throw new ToolError("give `view`");
        return MainThread.Run<ToolOutput>(() =>
        {
            ChatWatchMute();
            return GameViews.Open(view);
        });
    }

    internal static void ChatWatchMute() => GameWatch.IgnoreViewsFor(TimeSpan.FromSeconds(2));
}

/// <summary>A route drawn on the game's real map.</summary>
internal sealed class MapRouteTool : ITool
{
    public string Name => "map_route";

    public string Description =>
        "Open the game's map and draw a suggested route over it. `nodes` in order: r<row>c<col> (as in render_map with " +
        "coords), \"current\" or \"boss\". An empty list clears the route.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["nodes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["color"] = new JsonObject { ["type"] = "string", ["description"] = "Line color (name or #hex). Default gold." },
        },
        ["required"] = new JsonArray("nodes"),
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        var nodes = ChatToolArgs.Strings(args, "nodes");
        string? colorName = ChatToolArgs.Str(args, "color");
        return MainThread.Run<ToolOutput>(() =>
        {
            OpenViewTool.ChatWatchMute();
            var color = colorName != null && Color.HtmlIsValid(colorName.TrimStart('#'))
                ? Color.FromHtml(colorName)
                : colorName != null ? Color.FromString(colorName, new Color(1f, 0.84f, 0.3f, 0.9f)) : new Color(1f, 0.84f, 0.3f, 0.9f);
            return GameViews.DrawRoute(nodes, color);
        });
    }
}

/// <summary>The learning notes kept across runs (given to every play session in its brief).</summary>
internal sealed class NotesTool : ITool
{
    public string Name => "notes";

    public string Description =>
        "The learning notes kept across runs (lessons, the player's preferences). Without `text`: read them. " +
        "With `text`: replace them all (keep them short; merge, fix, or drop items as needed). Every play session gets " +
        "them in its run brief.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        if (args["text"] is JsonValue v && v.TryGetValue(out string? text) && text != null)
        {
            Prompts.SaveNotes(text);
            return Task.FromResult<ToolOutput>($"notes saved ({text.Trim().Length} chars)");
        }
        string notes = Prompts.Notes();
        return Task.FromResult<ToolOutput>(notes.Length > 0 ? notes : "(no notes yet)");
    }
}

/// <summary>The play level: how much the agent does in a run.</summary>
internal sealed class PlayLevelTool : ITool
{
    public string Name => "play_level";

    public string Description =>
        "The play level of runs: commentary (light comments), colow (the player leads, you gather reasons), cohigh " +
        "(you play, the player approves key choices), auto (you play the run). Without `level`: read it.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["level"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("commentary", "colow", "cohigh", "auto") },
        },
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        string? text = ChatToolArgs.Str(args, "level");
        return MainThread.Run<ToolOutput>(() =>
        {
            if (text == null) return $"play level: {Prompts.LevelKey(ChatSessions.Level)} ({Prompts.LevelName(ChatSessions.Level)})";
            var level = Prompts.ParseLevel(text) ?? throw new ToolError("level: commentary | colow | cohigh | auto");
            ChatSessions.SetLevel(level);
            return $"play level set to {Prompts.LevelKey(level)} ({Prompts.LevelName(level)}); the play session uses it from its next message";
        });
    }
}

/// <summary>The control session's brief for the next run's play session.</summary>
internal sealed class RunBriefTool : ITool
{
    public string Name => "run_brief";

    public string Description =>
        "A short brief for the next run's play session (this run's goal, things to watch, how the player wants to " +
        "play). Given once, with its first message. Without `text`: read the brief waiting for the next run.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        string? text = args["text"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        return MainThread.Run<ToolOutput>(() =>
        {
            if (text == null) return ChatSessions.Brief ?? "(no brief waiting)";
            ChatSessions.Brief = text;
            return "brief saved for the next run";
        });
    }
}

/// <summary>
/// Questions to the player with options (the same shape as Claude Code's own AskUserQuestion), shown in the chat
/// panel as radio buttons; the player's answer comes as their next message.
/// </summary>
internal sealed class AskUserQuestionTool : ITool
{
    public string Name => "AskUserQuestion";

    public string Description =>
        "Ask the player to choose, in the chat panel (the same as Claude Code's AskUserQuestion): 1-4 questions, each " +
        "with a short header (12 chars max, e.g. \"이번 턴\", \"족쇄 포션\"), the question, and 2-4 options " +
        "(label + description with the reason or numbers). Options show as radio buttons (check boxes with " +
        "multiSelect). Put the recommended option first and end its label with \"(추천)\". The player can always type " +
        "their own answer and add a note. Use it whenever the player has to choose (turn plans, rewards, paths, " +
        "potions). Not for choices already on the game screen (card rewards, events, map, shop, rest site): there, " +
        "recommend in text and let the player pick in the game. After calling it, end your turn: the answer comes as " +
        "the player's next message, one " +
        "line per question as \"<header>: <chosen label>\", then any note.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["questions"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = 4,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["question"] = new JsonObject { ["type"] = "string" },
                        ["header"] = new JsonObject { ["type"] = "string", ["maxLength"] = 12 },
                        ["options"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["minItems"] = 2,
                            ["maxItems"] = 4,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["label"] = new JsonObject { ["type"] = "string" },
                                    ["description"] = new JsonObject { ["type"] = "string" },
                                },
                                ["required"] = new JsonArray("label", "description"),
                            },
                        },
                        ["multiSelect"] = new JsonObject { ["type"] = "boolean" },
                    },
                    ["required"] = new JsonArray("question", "header", "options"),
                },
            },
        },
        ["required"] = new JsonArray("questions"),
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        var questions = Parse(args);
        return MainThread.Run<ToolOutput>(() =>
        {
            ChatToolArgs.RequirePanel();
            if (!ChatSessions.ShowQuestions(questions)) throw new ToolError("the chat panel is not ready");
            return $"shown {questions.Count} question(s) in the chat panel. End your turn now; the player's answer comes as their next message.";
        });
    }

    /// <summary>The questions of a call (also used by dev_layout to preview a card).</summary>
    public static List<AskQuestion> Parse(JsonObject args)
    {
        var questions = new List<AskQuestion>();
        foreach (var node in args["questions"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject q) continue;
            string question = ChatToolArgs.Str(q, "question") ?? throw new ToolError("each question needs `question`");
            string header = ChatToolArgs.Str(q, "header") ?? throw new ToolError("each question needs `header`");
            var options = (q["options"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(o => new AskOption(ChatToolArgs.Str(o, "label") ?? "", ChatToolArgs.Str(o, "description") ?? ""))
                .Where(o => o.Label.Length > 0)
                .ToList();
            if (options.Count < 2) throw new ToolError($"\"{header}\": give 2-4 options");
            bool multi = q["multiSelect"] is JsonValue m && m.TryGetValue(out bool b) && b;
            questions.Add(new AskQuestion(question, header, options.Take(6).ToList(), multi));
        }
        if (questions.Count == 0) throw new ToolError("give 1-4 questions");
        return questions.Take(4).ToList();
    }
}

/// <summary>Waits for the player's pick on the game screen (cohigh / auto), then the agent is told and goes on.</summary>
internal sealed class WaitForPlayerTool : ITool
{
    public string Name => "wait_for_player";

    public string Description =>
        "When the choice is on the game screen (card reward, event option, map path, shop, rest site, relic pick) and " +
        "the player should make it there: give your recommendation in text first, then call this and end your turn. " +
        "When the player picks in the game, you get an [자동] message with what happened, and go on.";

    public JsonObject InputSchema() => new() { ["type"] = "object", ["properties"] = new JsonObject() };

    public Task<ToolOutput> Call(JsonObject args) =>
        MainThread.Run<ToolOutput>(() => ChatSessions.WaitForPlayer()
            ? "waiting for the player's pick on the game screen. End your turn now."
            : "not in a run: nothing to wait for");
}
