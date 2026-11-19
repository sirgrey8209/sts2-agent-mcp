using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// The game's confirm popup (the one its menus use) driven by the mod: its text and buttons can change while it is up,
/// and it closes only when told to. The game's own answer wiring (<c>WaitForConfirmation</c>, which closes the popup on
/// either button) is not used; our handlers go on the buttons' Released signal, which their hotkeys (select / cancel)
/// also raise. Main thread only.
/// </summary>
internal sealed class GamePopup
{
    private readonly NGenericPopup _popup;
    private readonly NVerticalPopup _vertical;

    private GamePopup(NGenericPopup popup, NVerticalPopup vertical)
    {
        _popup = popup;
        _vertical = vertical;
    }

    /// <summary>The right button ("yes" side) was pressed.</summary>
    public event Action? Yes;
    /// <summary>The left button ("no" side) was pressed.</summary>
    public event Action? No;

    /// <summary>Null when the popup cannot be shown (no modal container, or another modal is up).</summary>
    public static GamePopup? Open(string title, string body, string? yes, string? no)
    {
        var modal = NModalContainer.Instance;
        if (modal == null || modal.OpenModal != null) return null;
        var popup = NGenericPopup.Create();
        if (popup == null) return null;
        modal.Add(popup);
        var vertical = popup.GetNode<NVerticalPopup>("VerticalPopup");
        var result = new GamePopup(popup, vertical);
        vertical.SetText(title, body); // also binds the button properties
        vertical.YesButton.IsYes = true;
        vertical.NoButton.IsYes = false;
        vertical.YesButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => result.Yes?.Invoke()));
        vertical.NoButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => result.No?.Invoke()));
        result.Set(title, body, yes, no);
        return result;
    }

    public bool IsOpen =>
        GodotObject.IsInstanceValid(_popup) && !_popup.IsQueuedForDeletion() && NModalContainer.Instance?.OpenModal == _popup;

    /// <summary>New text and buttons; a null button label hides that button.</summary>
    public void Set(string title, string body, string? yes, string? no)
    {
        if (!IsOpen) return;
        _vertical.SetText(title, body);
        _vertical.YesButton.Visible = yes != null;
        if (yes != null) _vertical.YesButton.SetText(yes);
        _vertical.NoButton.Visible = no != null;
        if (no != null) _vertical.NoButton.SetText(no);
    }

    public void Close()
    {
        if (IsOpen) NModalContainer.Instance!.Clear();
    }
}
