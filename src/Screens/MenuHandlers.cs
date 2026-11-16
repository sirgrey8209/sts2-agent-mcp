using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>Shared rendering/execution for screens whose actions are just their buttons.</summary>
internal static class ButtonScreen
{
    public static void Render(ScreenView view, List<UiButton> buttons, bool withKeys)
    {
        for (int i = 0; i < buttons.Count; i++)
            view.Add(withKeys ? $"  {i} {buttons[i].Key}: {buttons[i].Label}" : $"  {i} {buttons[i].Label}");
    }

    /// <summary>"choose 2" or "choose continue" (by key).</summary>
    public static UiButton? Pick(List<UiButton> buttons, ActionSpec action, out string? error)
    {
        error = null;
        string? arg = action.Arg(0);
        if (arg == null)
        {
            error = "usage: choose <i|key>";
            return null;
        }
        if (int.TryParse(arg, out int i))
        {
            if (i >= 0 && i < buttons.Count) return buttons[i];
        }
        else
        {
            var byKey = buttons.FirstOrDefault(b => b.Key.Equals(arg, StringComparison.OrdinalIgnoreCase));
            if (byKey != null) return byKey;
        }
        error = buttons.Count == 0 ? "no buttons available" : $"choose 0-{buttons.Count - 1} or a key";
        return null;
    }
}

/// <summary>Modal popups, confirmations and tutorials (anything in the modal container).</summary>
internal sealed class PopupHandler : IScreenHandler
{
    public string Kind => "popup";

    public bool Matches(GameCtx ctx) => NModalContainer.Instance?.OpenModal != null;

    private static Node? ModalNode() => NModalContainer.Instance?.OpenModal as Node;

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var node = ModalNode();
        view.Add(NModalContainer.Instance?.OpenModal?.GetType().Name ?? "modal");
        foreach (var text in Ui.Texts(node, 8)) view.Add("  " + text);
        ButtonScreen.Render(view, Ui.VisibleButtons(node), withKeys: false);
        view.Actions = "choose <i>";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        if (action.Verb != "choose") return ExecResult.Fail("a popup is open; use choose <i>");
        var button = ButtonScreen.Pick(Ui.VisibleButtons(ModalNode()), action, out string? error);
        if (button == null) return ExecResult.Fail(error!);
        button.Control.ForceClick();
        return ExecResult.Ok(SettleMode.Screen);
    }
}

/// <summary>
/// Main menu and its submenus. Buttons are discovered from "_xxxButton" fields, so new modes/buttons show up by key.
/// new_run is a macro: singleplayer → mode → lobby embark.
/// </summary>
internal sealed class MainMenuHandler : IScreenHandler
{
    public string Kind => "menu";

    public bool Matches(GameCtx ctx) => ctx.Screen is NMainMenu or NSubmenu;

    /// <summary>Single-player only: multiplayer entry points are not offered.</summary>
    private static List<UiButton> Buttons(object screen) =>
        Ui.FieldButtons(screen).Where(b => !b.Key.StartsWith("multiplayer", StringComparison.Ordinal)).ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        view.Add(ctx.Screen!.GetType().Name);
        var buttons = Buttons(ctx.Screen);
        ButtonScreen.Render(view, buttons, withKeys: true);
        view.Actions = "choose <i|key>";
        if (ctx.Screen is NMainMenu && buttons.Any(b => b.Key == "singleplayer"))
            view.Actions += " · new_run <CHARACTER> [asc=N] [seed=S] [mode=standard]";
        if (ctx.Screen is NSingleplayerSubmenu)
            view.Actions += " · mode <key>";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var buttons = Buttons(ctx.Screen!);
        switch (action.Verb)
        {
            case "choose":
            case "mode":
            {
                var button = ButtonScreen.Pick(buttons, action, out string? error);
                if (button == null) return ExecResult.Fail(error!);
                button.Control.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            }
            case "new_run":
            {
                if (ctx.Screen is not NMainMenu) return ExecResult.Fail("new_run is only available on the main menu");
                string? character = action.Arg(0);
                if (character == null) return ExecResult.Fail("usage: new_run <CHARACTER> [asc=N] [seed=S] [mode=standard]");
                var singleplayer = buttons.FirstOrDefault(b => b.Key == "singleplayer");
                if (singleplayer == null)
                    return ExecResult.Fail("singleplayer is not available (a saved run may exist: choose continue or abandon_run)");

                string mode = action.Options.GetValueOrDefault("mode", "standard");
                var embark = new List<string> { "embark", character };
                if (action.Options.TryGetValue("asc", out var asc)) embark.Add("asc=" + asc);
                if (action.Options.TryGetValue("seed", out var seed)) embark.Add("seed=" + seed);

                singleplayer.Control.ForceClick();
                return ExecResult.Ok(SettleMode.Screen, followups: new[] { "mode " + mode, string.Join(' ', embark) });
            }
            default:
                return ExecResult.Fail($"unknown menu action '{action.Verb}'");
        }
    }
}

/// <summary>
/// Any run-start screen backed by the game's StartRunLobby (standard, daily, custom, and likely future modes).
/// Settings go through the lobby API; embarking clicks the screen's embark button so the game's own flow runs.
/// </summary>
internal sealed class LobbyHandler : IScreenHandler
{
    public string Kind => "lobby";

    public bool Matches(GameCtx ctx) => ctx.Screen != null && FindLobby(ctx.Screen) != null;

    private static StartRunLobby? FindLobby(object screen)
    {
        for (var type = screen.GetType(); type != null && type != typeof(Node); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(StartRunLobby) && field.GetValue(screen) is StartRunLobby lobby) return lobby;
            }
        }
        return null;
    }

    private static List<NCharacterSelectButton> CharacterButtons(object screen) =>
        Ui.FindAll<NCharacterSelectButton>(screen as Node).Where(b => Ui.IsVisible(b)).ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var lobby = FindLobby(ctx.Screen!)!;
        view.Add($"{ctx.Screen!.GetType().Name} · mode {lobby.GameMode}");
        var local = lobby.LocalPlayer;
        view.Add($"character: {(local.character != null ? Text.Of(() => local.character.Title) + " (" + local.character.Id.Entry + ")" : "-")}");
        view.Add($"ascension: {lobby.Ascension} (max {lobby.MaxAscension}) · seed: {lobby.Seed ?? "random"}");

        var characters = CharacterButtons(ctx.Screen);
        if (characters.Count > 0)
        {
            view.Add("characters: " + string.Join(", ", characters.Select(b =>
                b.IsRandom ? "RANDOM" : b.Character.Id.Entry + (b.IsLocked ? " (locked)" : ""))));
        }

        var buttons = Ui.FieldButtons(ctx.Screen);
        ButtonScreen.Render(view, buttons, withKeys: true);
        view.Actions = "embark [CHARACTER] [asc=N] [seed=S] · choose <i|key>";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var screen = ctx.Screen!;
        var lobby = FindLobby(screen)!;
        switch (action.Verb)
        {
            case "choose":
            {
                var button = ButtonScreen.Pick(Ui.FieldButtons(screen), action, out string? error);
                if (button == null) return ExecResult.Fail(error!);
                button.Control.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            }
            case "embark":
                return Embark(screen, lobby, action);
            default:
                return ExecResult.Fail($"unknown lobby action '{action.Verb}'");
        }
    }

    private static ExecResult Embark(object screen, StartRunLobby lobby, ActionSpec action)
    {
        string? character = action.Arg(0);
        if (character != null)
        {
            var buttons = CharacterButtons(screen);
            var button = buttons.FirstOrDefault(b =>
                character.Equals("RANDOM", StringComparison.OrdinalIgnoreCase) ? b.IsRandom
                : !b.IsRandom && (b.Character.Id.Entry.Equals(character, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(Text.Of(() => b.Character.Title), character, StringComparison.OrdinalIgnoreCase)));
            if (button == null)
                return ExecResult.Fail($"character '{character}' is not selectable here"
                                       + (buttons.Count > 0 ? "; options: " + string.Join(", ", buttons.Select(b => b.IsRandom ? "RANDOM" : b.Character.Id.Entry)) : ""));
            if (button.IsLocked) return ExecResult.Fail($"{character} is locked");
            button.Select();
        }

        if (action.Options.TryGetValue("asc", out var ascText))
        {
            if (!int.TryParse(ascText, out int asc) || asc < 0) return ExecResult.Fail("asc must be a non-negative number");
            asc = Math.Min(asc, lobby.MaxAscension);
            if (Ui.GetField(screen, "_ascensionPanel") is NAscensionPanel panel) panel.SetAscensionLevel(asc);
            lobby.SyncAscensionChange(asc);
        }

        if (action.Options.TryGetValue("seed", out var seed)) lobby.SetSeed(seed);

        var embark = Ui.FieldButtons(screen).FirstOrDefault(b => b.Key is "embark" or "confirm");
        if (embark != null) embark.Control.ForceClick();
        else lobby.SetReady(true);

        return ExecResult.Ok(SettleMode.ScreenChange, timeout: TimeSpan.FromSeconds(40));
    }
}

/// <summary>Run end screen.</summary>
internal sealed class GameOverHandler : IScreenHandler
{
    public string Kind => "game_over";

    public bool Matches(GameCtx ctx) => ctx.Screen is NGameOverScreen;

    /// <summary>view_run opens a run-history view the screen context cannot see, so it is not offered.</summary>
    private static List<UiButton> Buttons(object screen) =>
        Ui.FieldButtons(screen).Where(b => b.Key != "view_run").ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        foreach (var text in Ui.Texts(ctx.Screen as Node, 10)) view.Add("  " + text);
        ButtonScreen.Render(view, Buttons(ctx.Screen!), withKeys: true);
        view.Actions = "choose <i|key>";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        if (action.Verb != "choose") return ExecResult.Fail("use choose <i|key>");
        var button = ButtonScreen.Pick(Buttons(ctx.Screen!), action, out string? error);
        if (button == null) return ExecResult.Fail(error!);
        button.Control.ForceClick();
        return ExecResult.Ok(SettleMode.Screen);
    }
}

/// <summary>
/// Fallback for screens without a dedicated handler (events, rest sites, shops, new game modes...):
/// lists the visible buttons of the current screen and clicks them by index.
/// </summary>
internal sealed class GenericHandler : IScreenHandler
{
    public string Kind => "generic";

    public bool Matches(GameCtx ctx) => true;

    private static Node ScreenNode(GameCtx ctx) => ctx.Screen as Node ?? Ui.Root;

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var node = ScreenNode(ctx);
        view.Add(ctx.Screen?.GetType().Name ?? "(no screen)");
        foreach (var text in Ui.Texts(node, full ? 30 : 12)) view.Add("  " + text);
        ButtonScreen.Render(view, Ui.VisibleButtons(node), withKeys: false);
        view.Actions = "click <i>";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        if (action.Verb is not ("click" or "choose")) return ExecResult.Fail($"this screen has no handler; use click <i> (got '{action.Verb}')");
        var button = ButtonScreen.Pick(Ui.VisibleButtons(ScreenNode(ctx)), action, out string? error);
        if (button == null) return ExecResult.Fail(error!);
        button.Control.ForceClick();
        return ExecResult.Ok(SettleMode.Screen);
    }
}
