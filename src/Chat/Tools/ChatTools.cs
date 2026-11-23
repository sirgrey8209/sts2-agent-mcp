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
using Sts2AgentMcp.Session;

namespace Sts2AgentMcp.Tools;

// Tools of the in-game chat's agents (docs/chat-tools.md), served only on the chat endpoint (/mcp/chat):
// look (quietly), info / info_grep (knowledge), open (the game's own views), mark (draw on the game, the drawer),
// operate (the chat's act), wait (be told when something happens), notes (memory-style learning notes).

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


/// <summary>Looks at the game without changing anything on the player's screen.</summary>
internal sealed class LookTool : ITool
{
    private readonly ActRunner _runner;

    public LookTool(ActRunner runner) => _runner = runner;

    public string Name => "look";

    public string Description =>
        "Look at the game quietly: never changes what the player sees. Returns the current screen, run status, what " +
        "can be done (the operate syntax) and the ids mark and operate use: hand cards c<n>, creatures e<n>/me/pet<n>, " +
        "potions by slot p<n>, options of the screen opt<n> (the number choose takes), map nodes r<row>c<col>. " +
        "view=full adds the deck, every pile and relic texts. operate returns the same view after acting.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["view"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("screen", "full"), ["description"] = "screen (default) or full" },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args) =>
        await _runner.RenderCurrent(ChatToolArgs.Str(args, "view") == "full");
}

/// <summary>Finds cards, relics and potions, and lines of the monster and event documents.</summary>
internal sealed class InfoGrepTool : ITool
{
    private readonly SearchTool _search = new();

    public string Name => "info_grep";

    public string Description =>
        "Find things to look up with info: cards, relics and potions by words in their name or text and by filters " +
        "(pool, type, rarity, cost, keyword), and lines of the monster and event documents that contain every word of " +
        "query (e.g. \"취약\", \"골드 잃\"). Returns ids for info.";

    public JsonObject InputSchema() => _search.InputSchema();

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var catalog = await _search.Call(args);
        string? query = ChatToolArgs.Str(args, "query");
        if (query == null) return catalog;
        var lines = KbDocs.Grep(query, 20);
        return lines.Count == 0 ? catalog : catalog.Text + "\n--- documents ---\n" + string.Join("\n", lines);
    }
}

/// <summary>Opens one of the game's own views; the player closes it.</summary>
internal sealed class OpenTool : ITool
{
    public string Name => "open";

    public string Description =>
        $"Open one of the game's own views on the player's screen to show what you are explaining: {GameViews.Help}. " +
        "The player sees it and closes it; nothing is played or chosen. To only read these, use look.";

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
            GameWatch.IgnoreViewsFor(TimeSpan.FromSeconds(2));
            return GameViews.Open(view);
        });
    }
}

/// <summary>Draws on the game screen (badges, circles, arrows, lines) and fills the drawer (combat_plan, overlay).</summary>
internal sealed class MarkTool : ITool
{
    public string Name => "mark";

    public string Description =>
        "Draw on the player's game screen to point at things, in your own ink colors (A blue, B green, C purple, then " +
        "amber, unless `color`). items[].kind: badge (a letter or number next to each target), circle, arrow (targets " +
        "[from, to], e.g. [\"c3\", \"e1\"]), line (targets in order; on map nodes it is a route), combat_plan (the drawer " +
        "beside the chat: up to 3 `plans` to pick from, each a name, steps of cards and potions with their targets and " +
        "a short `note` per step when it helps, a `summary` result, and a select button), overlay (the same drawer: " +
        "`cards` side by side with captions, short `lines`). Targets are look's ids (c<n>, e<n>, " +
        "me, p<n>, opt<n>, relic:<id>, deck_button, map_button, r<row>c<col>). Badges get A, B, C unless `label`. " +
        "`click` makes a badge or a plan pressable: answer (its label comes back as the player's message) or operate " +
        "(its `actions` run as if the player did them). Each call replaces your earlier marks unless keep=true; " +
        "clear=true removes them all. Marks and the drawer also go when the turn or the room changes. Call them by " +
        "their labels in the chat (\"A안\", \"1번\"), never by ids.";

    private static JsonObject Strings() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("badge", "circle", "arrow", "line", "combat_plan", "overlay") },
                        ["targets"] = Strings(),
                        ["label"] = new JsonObject { ["type"] = "string", ["maxLength"] = 2 },
                        ["color"] = new JsonObject { ["type"] = "string" },
                        ["click"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("answer", "operate") },
                        ["actions"] = Strings(),
                        ["title"] = new JsonObject { ["type"] = "string", ["description"] = "combat_plan / overlay: the drawer's title, e.g. 이번 턴" },
                        ["plans"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["maxItems"] = 3,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["label"] = new JsonObject { ["type"] = "string", ["maxLength"] = 2 },
                                    ["name"] = new JsonObject { ["type"] = "string", ["description"] = "a short name shown above the steps, e.g. 전부 공격" },
                                    ["recommended"] = new JsonObject { ["type"] = "boolean" },
                                    ["steps"] = new JsonObject
                                    {
                                        ["type"] = "array",
                                        ["items"] = new JsonObject
                                        {
                                            ["type"] = "object",
                                            ["properties"] = new JsonObject
                                            {
                                                ["card"] = new JsonObject { ["type"] = "string", ["description"] = "c<n>, or a card id or name" },
                                                ["potion"] = new JsonObject { ["type"] = "string", ["description"] = "p<n> (a potion slot), or a potion id or name" },
                                                ["target"] = new JsonObject { ["type"] = "string", ["description"] = "e<n> / me" },
                                                ["end_turn"] = new JsonObject { ["type"] = "boolean" },
                                                ["note"] = new JsonObject { ["type"] = "string", ["description"] = "only when it helps: what this step does, very short, e.g. -6, 처치, 방어 5, 취약 2" },
                                                ["text"] = new JsonObject { ["type"] = "string", ["description"] = "a step that is neither a card nor a potion, shown in a card's place (short)" },
                                            },
                                        },
                                    },
                                    ["summary"] = new JsonObject { ["type"] = "string", ["description"] = "the plan's result, shown right of the steps, parts split by \" · \", e.g. 받을 피해 0 · 처치 2" },
                                    ["click"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("answer", "operate") },
                                    ["actions"] = Strings(),
                                },
                                ["required"] = new JsonArray("steps"),
                            },
                        },
                        ["cards"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["card"] = new JsonObject { ["type"] = "string" },
                                    ["caption"] = new JsonObject { ["type"] = "string" },
                                },
                                ["required"] = new JsonArray("card"),
                            },
                        },
                        ["lines"] = Strings(),
                    },
                    ["required"] = new JsonArray("kind"),
                },
            },
            ["keep"] = new JsonObject { ["type"] = "boolean", ["description"] = "add to the earlier marks instead of replacing them" },
            ["clear"] = new JsonObject { ["type"] = "boolean", ["description"] = "remove every mark and close the drawer" },
        },
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        bool clear = args["clear"] is JsonValue c && c.TryGetValue(out bool cb) && cb;
        bool keep = args["keep"] is JsonValue k && k.TryGetValue(out bool kb) && kb;
        var nodes = (args["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
        return MainThread.Run<ToolOutput>(() =>
        {
            if (clear || nodes.Count == 0)
            {
                ScreenMarks.Clear();
                ChatLayout.Panel?.ClearDrawer();
                return "cleared";
            }
            var marks = new List<MarkItem>();
            var notes = new List<string>();
            foreach (var node in nodes)
            {
                string kind = ChatToolArgs.Str(node, "kind") ?? throw new ToolError("each item needs `kind`");
                if (kind is "combat_plan" or "overlay")
                {
                    ChatToolArgs.RequirePanel();
                    var content = Drawer(node, kind);
                    ChatLayout.Panel!.ShowDrawer(content);
                    notes.Add(kind == "combat_plan"
                        ? $"combat_plan: {content.Plans.Count} plan(s) {string.Join(", ", content.Plans.Select(p => p.Label))}"
                        : $"overlay: {content.Cards.Count} card(s), {content.Lines.Count} line(s)");
                    continue;
                }
                string? click = ChatToolArgs.Str(node, "click");
                var actions = ChatToolArgs.Strings(node, "actions");
                if (click == "operate" && actions.Count == 0) throw new ToolError("click=operate needs `actions`");
                marks.Add(new MarkItem(kind, ChatToolArgs.Strings(node, "targets"), Badge(ChatToolArgs.Str(node, "label")),
                    ParseColor(ChatToolArgs.Str(node, "color")), click, actions));
            }
            if (marks.Count > 0) notes.Add(ScreenMarks.Draw(marks, keep));
            else if (!keep) ScreenMarks.Clear();
            return string.Join("\n", notes);
        });
    }

    /// <summary>The drawer's content: plans for combat_plan, cards and lines for overlay.</summary>
    private static DrawerContent Drawer(JsonObject node, string kind)
    {
        var plans = new List<DrawerPlan>();
        int i = 0;
        var planNodes = (node["plans"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
        bool hasOverlay = node["cards"] is JsonArray { Count: > 0 } || node["lines"] is JsonArray { Count: > 0 };
        if (kind == "combat_plan" && planNodes.Count == 0) throw new ToolError("combat_plan: give `plans`");
        if (kind == "combat_plan" && hasOverlay) throw new ToolError("combat_plan takes `plans` only; cards and lines go in an overlay");
        if (kind == "overlay" && planNodes.Count > 0) throw new ToolError("plans go in a combat_plan, not an overlay");
        if (planNodes.Count > ChatPanel.MaxPlans) throw new ToolError($"combat_plan: {ChatPanel.MaxPlans} plans at most");
        foreach (var p in planNodes)
        {
            string label = Badge(ChatToolArgs.Str(p, "label")) ?? ((char)('A' + i)).ToString();
            var color = i < MapInk.RouteColors.Length ? MapInk.RouteColors[i] : MapInk.MarkColor;
            var steps = new List<DrawerStep>();
            foreach (var s in (p["steps"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                bool endTurn = s["end_turn"] is JsonValue e && e.TryGetValue(out bool eb) && eb;
                string? cardRef = ChatToolArgs.Str(s, "card");
                string? potionRef = ChatToolArgs.Str(s, "potion");
                string? target = ChatToolArgs.Str(s, "target");
                string? text = endTurn ? "턴 종료" : ChatToolArgs.Str(s, "text");
                CardModel? card = cardRef == null ? null : FindCard(cardRef) ?? throw new ToolError($"card '{cardRef}' not found");
                PotionModel? potion = card != null || potionRef == null ? null : FindPotion(potionRef) ?? throw new ToolError($"potion '{potionRef}' not found");
                if (card == null && potion == null && string.IsNullOrWhiteSpace(text)) throw new ToolError($"plan {label}: each step needs card, potion, end_turn or text");
                string? screenRef = card != null ? (Ids.ResolveCard(cardRef!) != null ? cardRef : null)
                    : potion != null && IsSlot(potionRef!) ? potionRef!.Trim().ToLowerInvariant() : null;
                steps.Add(new DrawerStep(card, potion, screenRef, target,
                    target != null ? ScreenTargets.DisplayName(target) : null, card == null && potion == null ? text : null,
                    ChatToolArgs.Str(s, "note")));
            }
            if (steps.Count == 0) throw new ToolError($"plan {label}: give `steps`");
            string click = ChatToolArgs.Str(p, "click") ?? "answer";
            var actions = ChatToolArgs.Strings(p, "actions");
            if (click == "operate" && actions.Count == 0) throw new ToolError($"plan {label}: click=operate needs `actions`");
            bool recommended = p["recommended"] is JsonValue r && r.TryGetValue(out bool rb) && rb;
            plans.Add(new DrawerPlan(label, ChatToolArgs.Str(p, "name"), recommended, steps, ChatToolArgs.Str(p, "summary"), click, actions, color));
            i++;
        }
        var cards = new List<DrawerCard>();
        foreach (var c in (node["cards"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            string id = ChatToolArgs.Str(c, "card") ?? throw new ToolError("each card needs `card`");
            cards.Add(new DrawerCard(FindCard(id) ?? throw new ToolError($"card '{id}' not found"), ChatToolArgs.Str(c, "caption")));
        }
        var lines = ChatToolArgs.Strings(node, "lines");
        if (plans.Count == 0 && cards.Count == 0 && lines.Count == 0) throw new ToolError("overlay: give `cards` or `lines`");
        return new DrawerContent(ChatToolArgs.Str(node, "title"), plans, cards, lines);
    }

    private static CardModel? FindCard(string reference) => Ids.ResolveCard(reference) ?? Catalog.FindCard(reference);

    private static bool IsSlot(string reference) => System.Text.RegularExpressions.Regex.IsMatch(reference.Trim(), @"^[pP]\d+$");

    /// <summary>A potion in the player's slot (p0) or from the game's catalog by id or name.</summary>
    private static PotionModel? FindPotion(string reference)
    {
        if (IsSlot(reference))
        {
            var me = GameCtx.Capture().Me;
            int slot = int.Parse(reference.Trim()[1..]);
            return me != null && slot < me.PotionSlots.Count ? me.PotionSlots[slot] : null;
        }
        return (Catalog.FindById(reference) ?? Catalog.FindByTitle(reference)) as PotionModel;
    }

    private static string? Badge(string? label) => label == null ? null : label.Length <= 2 ? label : label[..2];

    private static Color? ParseColor(string? name)
    {
        if (name == null) return null;
        if (Color.HtmlIsValid(name.TrimStart('#'))) return Color.FromHtml(name);
        var parsed = Color.FromString(name, new Color(0, 0, 0, 0));
        if (parsed.A == 0) throw new ToolError($"unknown color '{name}'");
        return parsed;
    }
}

/// <summary>Asks to be told when something happens in the game (the player picks, the turn or the room changes).</summary>
internal sealed class WaitTool : ITool
{
    public string Name => "wait";

    public string Description =>
        "Be told when something happens in the game, then end your turn: pick (the player picks on the game screen: " +
        "a reward, a path, an option), turn (a new turn starts), combat_end, room (the player enters the next room). " +
        "A message from the player also ends the wait. You get an [자동] message when it happens.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["for"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("pick", "turn", "combat_end", "room") },
            },
        },
        ["required"] = new JsonArray("for"),
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        var kinds = ChatToolArgs.Strings(args, "for");
        if (kinds.Count == 0) throw new ToolError("give `for`");
        return MainThread.Run<ToolOutput>(() => ChatSessions.Wait(kinds)
            ? $"waiting for: {string.Join(", ", kinds)}. End your turn now."
            : "not in a run: nothing to wait for");
    }
}

/// <summary>The learning notes, as Claude's memory tool: files under /notes.</summary>
internal sealed class NotesTool : ITool
{
    public string Name => "notes";

    public string Description =>
        "Your learning notes kept across runs, as files under /notes (like the memory tool). command: view (a folder " +
        "or a file; view_range [from, to] lines), create (path, file_text), str_replace (path, old_str, new_str; " +
        "old_str must be unique), insert (path, insert_line, insert_text: after that line, 0 = top), delete (path), " +
        "rename (old_path, new_path). /notes/general.md holds lessons for every run, /notes/characters/<id>.md per " +
        "character (ironclad, silent, defect, necrobinder, regent); add other files as needed. The run brief gives you " +
        "the file list, general.md and this character's file. Keep notes short and correct: fix or merge, do not pile up.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("view", "create", "str_replace", "insert", "delete", "rename") },
            ["path"] = new JsonObject { ["type"] = "string" },
            ["view_range"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
            ["file_text"] = new JsonObject { ["type"] = "string" },
            ["old_str"] = new JsonObject { ["type"] = "string" },
            ["new_str"] = new JsonObject { ["type"] = "string" },
            ["insert_line"] = new JsonObject { ["type"] = "integer" },
            ["insert_text"] = new JsonObject { ["type"] = "string" },
            ["old_path"] = new JsonObject { ["type"] = "string" },
            ["new_path"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("command"),
    };

    public Task<ToolOutput> Call(JsonObject args)
    {
        string? Raw(string key) => args[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        string command = ChatToolArgs.Str(args, "command") ?? throw new ToolError("give `command`");
        ToolOutput result = command switch
        {
            "view" => NotesStore.View(Raw("path"),
                args["view_range"] is JsonArray r ? r.Select(n => n is JsonValue v && v.TryGetValue(out int x) ? x : 0).ToArray() : null),
            "create" => NotesStore.Create(Raw("path"), Raw("file_text")),
            "str_replace" => NotesStore.StrReplace(Raw("path"), Raw("old_str"), Raw("new_str")),
            "insert" => NotesStore.Insert(Raw("path"), args["insert_line"] is JsonValue l && l.TryGetValue(out int line) ? line : -1, Raw("insert_text")),
            "delete" => NotesStore.Delete(Raw("path")),
            "rename" => NotesStore.Rename(Raw("old_path"), Raw("new_path")),
            _ => throw new ToolError($"unknown command '{command}'"),
        };
        return Task.FromResult(result);
    }
}

/// <summary>
/// Questions to the player with options, shown in the chat panel as a card. No longer offered to the chat's agents
/// (plans in the drawer took its place); kept for dev_layout's preview of the card until the card is removed.
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

