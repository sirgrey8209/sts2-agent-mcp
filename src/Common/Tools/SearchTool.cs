using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Tools;

internal sealed class SearchTool : ITool
{
    public string Name => "search";

    public string Description =>
        "Search cards, relics and potions by text and filters; one line per match (id, name, cost, type, rarity, pool, " +
        "description in the game's language). Text matches name, description and id; several words must all match. " +
        "Use `info` for full details of one item (cards: upgrade included) and `render_cards` with card ids to show them.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = Str("Words to find in name/description/id, e.g. \"영창\" or \"draw exhaust\"."),
            ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("card", "relic", "potion") },
            ["pool"] = Str("Character or pool: ironclad, silent, defect, necrobinder, regent (or the displayed character " +
                           "name); cards also colorless, curse, status, event, token, quest; relics/potions shared, event."),
            ["type"] = Str("Card type: Attack, Skill, Power, Status, Curse, Quest."),
            ["rarity"] = Str("Cards: Basic, Common, Uncommon, Rare, Ancient, Event, Token, Status, Curse. Relics: Starter, " +
                             "Common, Uncommon, Rare, Shop, Event, Ancient. Potions: Common, Uncommon, Rare, Event, Token."),
            ["cost"] = Str("Card energy cost: 0, 1, 2, ... or X."),
            ["keyword"] = Str("Card keyword: Exhaust, Ethereal, Innate, Unplayable, Retain, Sly, Eternal."),
            ["upgraded"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Cards: search and show the upgraded versions (non-upgradable cards are left out).",
            },
            ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Maximum lines (default 40, max 200)." },
        },
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        var q = new SearchQuery(
            Opt(args, "query"), Opt(args, "kind"), Opt(args, "pool"), Opt(args, "type"), Opt(args, "rarity"),
            Opt(args, "cost"), Opt(args, "keyword"),
            args["upgraded"] is JsonValue u && u.TryGetValue(out bool b) && b,
            args["limit"] is JsonValue l && l.TryGetValue(out int n) ? n : 40);
        if (!Features.IsEnabled("catalog")) throw new ToolError("search is unavailable: " + Features.Summary());
        return await MainThread.Run(() => Search.Run(q));
    }

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    private static string? Opt(JsonObject args, string key)
    {
        if (args[key] is not JsonValue v) return null;
        if (v.TryGetValue(out string? s)) return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        if (v.TryGetValue(out int i)) return i.ToString(); // cost given as a number
        return null;
    }
}
