using Godot;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Chat panel: its edge. A thin line between the chat and the game with a small grip in its middle to resize the chat
/// (the line itself is not grabbed), a fold button in the header, and, while folded (<see cref="ChatLayout.Collapsed"/>:
/// the game has the whole window), only a tab with the Neow icon at the middle of the left edge to open it again, with a
/// dot when something new came in meanwhile.
/// </summary>
internal sealed partial class ChatPanel
{
    private const float HandleWidth = 14;
    private const float HandleHeight = 72;
    private const float FoldButtonSize = 40;
    private const float TabIcon = 56;
    private static readonly Vector2 TabSize = new(TabIcon + 18, TabIcon + 24);
    /// <summary>The game's gold, for the grip and the tab while hovered or held.</summary>
    private static readonly Color Accent = new(0.94f, 0.78f, 0.32f, 1f);

    private Panel _handle = null!;
    private StyleBoxFlat _handleStyle = null!;
    private ColorRect[] _grip = null!;
    private float _grabOffset;
    private Button _fold = null!;
    private Panel _tab = null!;
    private StyleBoxFlat _tabStyle = null!;
    private Panel _unread = null!;

    private void BuildEdge()
    {
        // The grip: a small rounded slab across the line, three dots on it.
        _handleStyle = new StyleBoxFlat { BgColor = ChatTheme.HeaderColor, BorderColor = LineIdle };
        _handleStyle.SetBorderWidthAll(2);
        _handleStyle.SetCornerRadiusAll(7);
        _handle = new Panel
        {
            Size = new Vector2(HandleWidth, HandleHeight),
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Hsize,
        };
        _handle.AddThemeStyleboxOverride("panel", _handleStyle);
        _grip = new ColorRect[3];
        for (int i = 0; i < _grip.Length; i++)
        {
            _grip[i] = new ColorRect
            {
                Color = ChatTheme.DimColor,
                Size = new Vector2(4, 4),
                Position = new Vector2((HandleWidth - 4) / 2, HandleHeight / 2 - 12 + i * 10),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _handle.AddChild(_grip[i]);
        }
        _handle.GuiInput += OnDividerInput;
        _handle.MouseEntered += () => { _hovered = true; UpdateDividerColor(); };
        _handle.MouseExited += () => { _hovered = false; UpdateDividerColor(); };
        Layer.AddChild(_handle);

        // Fold: a chevron pointing left, in the header.
        _fold = new Button { Size = new Vector2(FoldButtonSize, FoldButtonSize), CustomMinimumSize = new Vector2(FoldButtonSize, FoldButtonSize) };
        ChatTheme.StyleButton(_fold, ChatTheme.PlainButton, ChatTheme.SmallFontSize);
        _fold.AddChild(new Line2D
        {
            Points = new[] { new Vector2(23, 12), new Vector2(15, 20), new Vector2(23, 28) },
            Width = 3,
            DefaultColor = ChatTheme.TextColor,
            JointMode = Line2D.LineJointMode.Round,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        });
        _fold.Pressed += () => Fold(true);
        Layer.AddChild(_fold);

        // The tab left while folded: rounded on the right, flush with the window's left edge.
        _tabStyle = new StyleBoxFlat { BgColor = ChatTheme.HeaderColor, BorderColor = LineIdle };
        _tabStyle.SetBorderWidthAll(2);
        _tabStyle.BorderWidthLeft = 0;
        _tabStyle.CornerRadiusTopRight = _tabStyle.CornerRadiusBottomRight = 16;
        _tab = new Panel
        {
            Size = TabSize,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        _tab.AddThemeStyleboxOverride("panel", _tabStyle);
        var icon = Icon(ChatTheme.AgentIcon, ChatTheme.AgentOutline, TabIcon);
        icon.Position = new Vector2(6, (TabSize.Y - TabIcon) / 2);
        _tab.AddChild(icon);
        var dotStyle = new StyleBoxFlat { BgColor = Accent, BorderColor = ChatTheme.PanelColor };
        dotStyle.SetBorderWidthAll(2);
        dotStyle.SetCornerRadiusAll(8);
        _unread = new Panel { Size = new Vector2(16, 16), Position = new Vector2(TabSize.X - 22, 6), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _unread.AddThemeStyleboxOverride("panel", dotStyle);
        _tab.AddChild(_unread);
        _tab.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                _tab.AcceptEvent();
                Fold(false);
            }
        };
        _tab.MouseEntered += () => _tabStyle.BorderColor = Accent;
        _tab.MouseExited += () => _tabStyle.BorderColor = LineIdle;
        Layer.AddChild(_tab);
    }

    private void Fold(bool folded)
    {
        if (folded) Input.ReleaseFocus();
        else _unread.Visible = false;
        ChatLayout.SetCollapsed(folded);
    }

    /// <summary>Something new in the chat: marks the tab while folded.</summary>
    private void NoteUnread()
    {
        if (ChatLayout.Collapsed) _unread.Visible = true;
    }

    /// <param name="strip">The chat's area in panel units (empty while folded).</param>
    /// <param name="height">The window height in panel units.</param>
    private void LayoutEdge(Rect2 strip, float height)
    {
        bool folded = ChatLayout.Collapsed;
        _dividerLine.Position = new Vector2(strip.End.X - DividerLine, strip.Position.Y);
        _dividerLine.Size = new Vector2(DividerLine, strip.Size.Y);
        _dividerLine.Visible = !folded && strip.Size.X > DividerHit;
        _handle.Position = new Vector2(strip.End.X - HandleWidth / 2, strip.Position.Y + (strip.Size.Y - HandleHeight) / 2);
        _handle.Visible = _dividerLine.Visible;

        _fold.Position = strip.Position + new Vector2(strip.Size.X - DividerHit - Pad - FoldButtonSize, (HeaderHeight - FoldButtonSize) / 2);
        _fold.Visible = _hasRoom && !folded;

        _tab.Position = new Vector2(0, (height - TabSize.Y) / 2);
        _tab.Visible = folded;
    }

    /// <summary>Room the fold button takes at the header's right (with its gap).</summary>
    private const float FoldRoom = FoldButtonSize + 10;
}
