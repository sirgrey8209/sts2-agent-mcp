using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.DevConsole;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Tools;

/// <summary>
/// Development only: runs a command of the game's developer console (room, event, fight, card, relic, die, ...).
/// Registered only when the config has "dev_tools": true, so it never shows up for normal play.
/// </summary>
internal sealed class DevConsoleTool : ITool
{
    private DevConsole? _console;

    public string Name => "dev_console";

    public string Description =>
        "DEVELOPMENT/TESTING ONLY (cheats): run a command of the game's developer console, e.g. \"room RestSite\", " +
        "\"event TheArchitect\", \"fight <encounter>\", \"card <id>\", \"relic <id>\", \"gold 100\", \"die\", \"help\". " +
        "Never use during the user's real runs. Call `state` afterwards.";

    public JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject { ["type"] = "string", ["description"] = "Console command line." },
        },
        ["required"] = new JsonArray("command"),
    };

    public async Task<ToolOutput> Call(JsonObject args)
    {
        string? command = args["command"] is JsonValue v && v.TryGetValue(out string? s) ? s?.Trim() : null;
        if (string.IsNullOrEmpty(command)) throw new ToolError("`command` is required");

        // Mod-side extra (not a game command): "profile <n>" clicks profile n on the open profile screen, so tests
        // can run on a spare profile. The regular tools never switch profiles.
        if (command.StartsWith("profile ", StringComparison.OrdinalIgnoreCase))
            return await MainThread.Run(() => SwitchProfile(command[8..].Trim()));
        // "event_any <ID>": like the game's "event" command, but for any event model, including ones outside the
        // regular event pools (THE_ARCHITECT).
        if (command.StartsWith("event_any ", StringComparison.OrdinalIgnoreCase))
        {
            string entry = command[10..].Trim().ToUpperInvariant();
            var task = await MainThread.Run(() =>
            {
                var model = MegaCrit.Sts2.Core.Models.ModelDb.GetByIdOrNull<MegaCrit.Sts2.Core.Models.EventModel>(
                    new MegaCrit.Sts2.Core.Models.ModelId(
                        MegaCrit.Sts2.Core.Models.ModelId.SlugifyCategory<MegaCrit.Sts2.Core.Models.EventModel>(), entry))
                    ?? throw new ToolError($"no event model {entry}");
                var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance.DebugOnlyGetState()
                          ?? throw new ToolError("no run in progress");
                // Same steps as the game's "event" command: record the map point, then enter the room.
                run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown,
                    MegaCrit.Sts2.Core.Rooms.RoomType.Event, model.Id);
                // Entering a room does not close an open map screen (normal travel closes it itself).
                if (MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance is { IsOpen: true } map) map.Close(animateOut: false);
                return MegaCrit.Sts2.Core.Runs.RunManager.Instance.EnterRoom(new MegaCrit.Sts2.Core.Rooms.EventRoom(model));
            });
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
            return task.IsFaulted ? "error: " + task.Exception?.GetBaseException().Message : $"entered event {entry}";
        }

        var result = await MainThread.Run(() =>
        {
            _console ??= new DevConsole(shouldAllowDebugCommands: true);
            return _console.ProcessCommand(command);
        });
        string text = (result.success ? "ok" : "failed") + (string.IsNullOrEmpty(result.msg) ? "" : ": " + result.msg);
        if (result.task != null)
        {
            var done = await Task.WhenAny(result.task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (done != result.task) text += " (still running after 30s)";
            else if (result.task.IsFaulted) text += " (error: " + result.task.Exception?.GetBaseException().Message + ")";
        }
        return text;
    }

    private static string SwitchProfile(string which)
    {
        if (!int.TryParse(which, out int n) || n < 1) throw new ToolError("usage: profile <1|2|3> (open the profile screen first)");
        var buttons = Game.Ui.FindAllByPosition<MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen.NProfileButton>(Game.Ui.Root)
            .Where(Game.Ui.IsVisible).ToList();
        if (buttons.Count == 0) throw new ToolError("no profile screen open (main menu: choose open_profile_screen)");
        if (n > buttons.Count) throw new ToolError($"only {buttons.Count} profile slots");
        buttons[n - 1].ForceClick();
        return $"clicked profile slot {n} of {buttons.Count}";
    }
}
