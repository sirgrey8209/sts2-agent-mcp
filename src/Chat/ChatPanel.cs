using System;
using System.Collections.Generic;
using Godot;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// The chat panel in the strip left of the game: a messenger built from the game's Ancient dialogue line look
/// (see <see cref="ChatTheme"/>), plus masks over anything the game draws outside its own rect and a divider to resize.
/// Panel units: window pixels / u, with u chosen so the window is 1080 units tall; the layer maps them back to window
/// pixels through the root's (non-uniform) stretch, so text is never distorted. Main thread only.
/// </summary>
internal sealed class ChatPanel
{
    public const string NodeName = "AgentChatPanel";

    private const float Pad = 20;
    private const float HeaderHeight = 84;
    private const float InputHeight = 64;
    private const float IconSize = 52;
    private const float DividerHit = 12;   // grab area, panel units (inside the panel, at its right edge)
    private const float DividerLine = 2;
    private static readonly Color LineIdle = new(0.22f, 0.25f, 0.32f, 1f);
    private static readonly Color LineActive = new(0.55f, 0.62f, 0.78f, 1f);

    private readonly ColorRect _background;
    private readonly ColorRect[] _masks = new ColorRect[3];
    private readonly ColorRect _headerBg;
    private readonly Control _headerIcon;
    private readonly Label _name;
    private readonly Label _status;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _list;
    private readonly ColorRect _dividerLine;
    private readonly Control _divider;
    private readonly List<BubbleRow> _bubbles = new();
    private BubbleRow? _typing;
    private bool _dragging;
    private bool _hovered;
    private bool _stickToBottom = true;

    public ChatPanel(Window root)
    {
        Layer = new CanvasLayer { Name = NodeName, Layer = 120 };
        _background = Rect(ChatTheme.PanelColor);
        for (int i = 0; i < _masks.Length; i++) _masks[i] = Rect(new Color(0, 0, 0, 1));

        _headerBg = Rect(ChatTheme.HeaderColor);
        _headerIcon = Icon(ChatTheme.AgentIcon, ChatTheme.AgentOutline, IconSize);
        Layer.AddChild(_headerIcon);
        _name = new Label { Text = "Claude", MouseFilter = Control.MouseFilterEnum.Ignore };
        ChatTheme.StyleText(_name, 28);
        if (ChatTheme.Bold != null) _name.AddThemeFontOverride("font", ChatTheme.Bold);
        _status = new Label { Text = "메시지를 보내면 연결해요", MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true };
        ChatTheme.StyleText(_status, ChatTheme.SmallFontSize, ChatTheme.DimColor);
        Layer.AddChild(_name);
        Layer.AddChild(_status);

        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 14);
        _scroll.AddChild(_list);
        Layer.AddChild(_scroll);
        // Follow new messages unless the player scrolled up to read.
        _list.Resized += () => { if (_stickToBottom) ScrollToBottom(); };
        _scroll.GetVScrollBar().ValueChanged += value =>
        {
            var bar = _scroll.GetVScrollBar();
            _stickToBottom = value >= bar.MaxValue - bar.Page - 4;
        };

        Input = new LineEdit
        {
            PlaceholderText = "메시지 입력 (Enter 전송, Esc 게임으로)",
            KeepEditingOnTextSubmit = true,
            CaretBlink = true,
        };
        ChatTheme.StyleText(Input, 22);
        Input.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, 0.35f));
        Input.AddThemeStyleboxOverride("normal", ChatTheme.InputStyle(false));
        Input.AddThemeStyleboxOverride("focus", ChatTheme.InputStyle(true));
        Input.AddThemeStyleboxOverride("read_only", ChatTheme.InputStyle(false));
        Input.TextSubmitted += OnSubmitted;
        Input.GuiInput += ev =>
        {
            if (ev is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                Input.ReleaseFocus();
                Input.AcceptEvent();
            }
        };
        Layer.AddChild(Input);

        _dividerLine = new ColorRect { Color = LineIdle, MouseFilter = Control.MouseFilterEnum.Ignore };
        _divider = new Control { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Hsize };
        _divider.GuiInput += OnDividerInput;
        _divider.MouseEntered += () => { _hovered = true; UpdateDividerColor(); };
        _divider.MouseExited += () => { _hovered = false; UpdateDividerColor(); };
        Layer.AddChild(_dividerLine);
        Layer.AddChild(_divider);
        root.AddChild(Layer);
    }

    public CanvasLayer Layer { get; }
    public LineEdit Input { get; }

    /// <summary>Raised with the text the player sent.</summary>
    public event Action<string>? Submitted;

    public void SetVisible(bool visible) => Layer.Visible = visible;

    // ---- messages ----

    public void AddUser(string text)
    {
        var (icon, outline, color) = ChatTheme.Player();
        AddBubble(new BubbleRow(text, right: true, icon, outline, color));
    }

    public void AddAgent(string text)
    {
        ShowTyping(false);
        AddBubble(new BubbleRow(text, right: false, ChatTheme.AgentIcon, ChatTheme.AgentOutline, ChatTheme.AgentColor));
    }

    /// <summary>A small centered line: tool use, connection state, errors.</summary>
    public void AddNote(string text, bool error = false)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ChatTheme.StyleText(label, ChatTheme.SmallFontSize, error ? ChatTheme.ErrorColor : ChatTheme.NoteColor);
        Append(label);
    }

    /// <summary>An agent bubble with "· · ·" while the agent works.</summary>
    public void ShowTyping(bool on)
    {
        if (on && _typing == null)
        {
            _typing = new BubbleRow("· · ·", right: false, ChatTheme.AgentIcon, ChatTheme.AgentOutline, ChatTheme.AgentColor);
            AddBubble(_typing);
        }
        else if (!on && _typing != null)
        {
            _bubbles.Remove(_typing);
            _typing.Row.QueueFree();
            _typing = null;
        }
    }

    public void SetStatus(string text) => _status.Text = text;

    private void AddBubble(BubbleRow row)
    {
        _bubbles.Add(row);
        Append(row.Row);
        row.Fit(RowWidth);
    }

    private void Append(Control control)
    {
        // Keep the typing indicator last.
        _list.AddChild(control);
        if (_typing != null && control != _typing.Row) _list.MoveChild(_typing.Row, -1);
        _stickToBottom = true;
        ScrollToBottom();
    }

    private void ScrollToBottom() =>
        Callable.From(() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue).CallDeferred();

    private float RowWidth => Mathf.Max(80, _scroll.Size.X - 16);

    /// <summary>Sends text as if typed (development tool).</summary>
    public void Submit(string text) => OnSubmitted(text);

    private void OnSubmitted(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Input.Clear();
        Submitted?.Invoke(text);
    }

    // ---- layout ----

    /// <param name="stretch">Root content units to window pixels.</param>
    /// <param name="win">Window size in pixels.</param>
    /// <param name="game">Where the game is drawn, in window pixels.</param>
    public void Layout(Transform2D stretch, Vector2 win, Rect2 game)
    {
        float u = win.Y / 1080f;
        Layer.Transform = stretch.AffineInverse() * new Transform2D(0, new Vector2(u, u), 0, Vector2.Zero);
        Rect2 R(float x0, float y0, float x1, float y1) => new(x0 / u, y0 / u, Mathf.Max(0, x1 - x0) / u, Mathf.Max(0, y1 - y0) / u);

        Place(_masks[0], R(game.Position.X, 0, win.X, game.Position.Y));
        Place(_masks[1], R(game.Position.X, game.End.Y, win.X, win.Y));
        Place(_masks[2], R(game.End.X, 0, win.X, win.Y));

        var strip = R(0, 0, game.Position.X, win.Y);
        Place(_background, strip);
        float inner = Mathf.Max(0, strip.Size.X - 2 * Pad - DividerHit);

        Place(_headerBg, new Rect2(strip.Position, new Vector2(strip.Size.X, HeaderHeight)));
        _headerIcon.Position = strip.Position + new Vector2(Pad, (HeaderHeight - IconSize) / 2);
        float textX = Pad + IconSize + 14;
        _name.Position = strip.Position + new Vector2(textX, 10);
        _name.Size = new Vector2(Mathf.Max(0, strip.Size.X - textX - Pad), 36);
        _status.Position = strip.Position + new Vector2(textX, 48);
        _status.Size = new Vector2(Mathf.Max(0, strip.Size.X - textX - Pad - DividerHit), 26);

        _scroll.Position = strip.Position + new Vector2(Pad, HeaderHeight + 12);
        _scroll.Size = new Vector2(inner, Mathf.Max(0, strip.Size.Y - HeaderHeight - 12 - InputHeight - 2 * Pad));

        Input.Position = strip.Position + new Vector2(Pad, strip.Size.Y - InputHeight - Pad);
        Input.Size = new Vector2(inner, InputHeight);

        bool hasRoom = strip.Size.X >= 160;
        foreach (var c in new Control[] { _headerBg, _headerIcon, _name, _status, _scroll, Input }) c.Visible = hasRoom;

        _divider.Position = new Vector2(strip.End.X - DividerHit, strip.Position.Y);
        _divider.Size = new Vector2(DividerHit, strip.Size.Y);
        _dividerLine.Position = new Vector2(strip.End.X - DividerLine, strip.Position.Y);
        _dividerLine.Size = new Vector2(DividerLine, strip.Size.Y);
        _divider.Visible = _dividerLine.Visible = strip.Size.X > DividerHit;

        foreach (var bubble in _bubbles) bubble.Fit(RowWidth);
    }

    private ColorRect Rect(Color color)
    {
        var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Stop };
        Layer.AddChild(rect);
        return rect;
    }

    private static void Place(ColorRect rect, Rect2 r)
    {
        rect.Position = r.Position;
        rect.Size = r.Size;
        rect.Visible = r.Size.X > 0.5f && r.Size.Y > 0.5f;
    }

    /// <summary>Character/ancient icon with its outline behind, as in the dialogue line and run history.</summary>
    internal static Control Icon(Texture2D? icon, Texture2D? outline, float size)
    {
        var box = new Control { CustomMinimumSize = new Vector2(size, size), Size = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (outline != null)
            box.AddChild(Fill(new TextureRect { Texture = outline, Modulate = new Color(0, 0, 0, 0.502f) }));
        if (icon != null)
            box.AddChild(Fill(new TextureRect { Texture = icon }));
        return box;
    }

    private static TextureRect Fill(TextureRect rect)
    {
        rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    // ---- divider ----

    private void OnDividerInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                if (!_dragging) ChatLayout.ResizeChatDone();
                UpdateDividerColor();
                _divider.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                // GUI input positions are local to the divider; the divider sits directly in the panel layer, so
                // adding its position gives panel units. While the button is held it keeps receiving motion even
                // over the game.
                ChatLayout.ResizeChat(_divider.Position.X + motion.Position.X + DividerHit / 2);
                _divider.AcceptEvent();
                break;
        }
    }

    private void UpdateDividerColor() => _dividerLine.Color = _dragging || _hovered ? LineActive : LineIdle;

    /// <summary>
    /// One message: icon + tail + nine-patch bubble with the text, laid out like the Ancient dialogue line
    /// (bubble and text overlap inside a MarginContainer; tails overlap the bubble by 12).
    /// </summary>
    private sealed class BubbleRow
    {
        private const float IconGap = 8;
        private const float FarGap = 36;   // space kept free on the other side, so both speakers stay distinguishable

        private readonly RichTextLabel _text;
        private readonly string _plain;
        private readonly bool _right;
        private readonly bool _hasIcon;

        public BubbleRow(string text, bool right, Texture2D? icon, Texture2D? outline, Color color)
        {
            _right = right;
            _plain = ChatTheme.ToPlain(text);
            _hasIcon = icon != null;

            Row = new HBoxContainer
            {
                Alignment = right ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            Row.AddThemeConstantOverride("separation", (int)IconGap);

            var dialogue = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, MouseFilter = Control.MouseFilterEnum.Ignore };
            var shape = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            shape.AddThemeConstantOverride("separation", -(int)ChatTheme.TailOverlap);
            var bubble = Bubble(color);
            bubble.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (!right) shape.AddChild(Tail(color, flip: false));
            shape.AddChild(bubble);
            if (right) shape.AddChild(Tail(color, flip: true));
            dialogue.AddChild(shape);

            var textBox = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            float tail = ChatTheme.TailWidth - ChatTheme.TailOverlap;
            textBox.AddThemeConstantOverride("margin_left", ChatTheme.TextMarginLeft + (right ? 0 : (int)tail));
            textBox.AddThemeConstantOverride("margin_right", ChatTheme.TextMarginRight + (right ? (int)tail : 0));
            textBox.AddThemeConstantOverride("margin_top", ChatTheme.TextMarginTop);
            textBox.AddThemeConstantOverride("margin_bottom", ChatTheme.TextMarginBottom);
            _text = new RichTextLabel
            {
                BbcodeEnabled = true,
                Text = ChatTheme.ToBbcode(text),
                FitContent = true,
                ScrollActive = false,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SelectionEnabled = true,
                ContextMenuEnabled = true,
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            ChatTheme.StyleText(_text);
            textBox.AddChild(_text);
            dialogue.AddChild(textBox);

            var iconBox = Icon(icon, outline, IconSize);
            iconBox.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            iconBox.Visible = _hasIcon;
            if (right)
            {
                Row.AddChild(dialogue);
                Row.AddChild(iconBox);
            }
            else
            {
                Row.AddChild(iconBox);
                Row.AddChild(dialogue);
            }
        }

        public HBoxContainer Row { get; }

        /// <summary>Bubble hugs short text and wraps long text at the row's free width.</summary>
        public void Fit(float rowWidth)
        {
            float tail = ChatTheme.TailWidth - ChatTheme.TailOverlap;
            float chrome = tail + ChatTheme.TextMarginLeft + ChatTheme.TextMarginRight;
            float maxText = Mathf.Max(40, rowWidth - (_hasIcon ? IconSize + IconGap : 0) - FarGap - chrome);
            float natural = ChatTheme.Regular?.GetMultilineStringSize(_plain, HorizontalAlignment.Left, -1, ChatTheme.FontSize).X ?? maxText;
            _text.CustomMinimumSize = new Vector2(Mathf.Min(maxText, natural + 4), 0);
        }

        private static NinePatchRect Bubble(Color color)
        {
            var bubble = Patch(ChatTheme.Bubble, color);
            bubble.CustomMinimumSize = new Vector2(2 * ChatTheme.PatchH, 2 * ChatTheme.PatchV);
            var shadow = Patch(ChatTheme.Bubble, ChatTheme.ShadowColor);
            shadow.ShowBehindParent = true;
            shadow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            shadow.OffsetLeft = shadow.OffsetRight = ChatTheme.BubbleShadowOffset.X;
            shadow.OffsetTop = shadow.OffsetBottom = ChatTheme.BubbleShadowOffset.Y;
            bubble.AddChild(shadow);
            return bubble;
        }

        private static NinePatchRect Patch(Texture2D? texture, Color color)
        {
            var patch = new NinePatchRect
            {
                Texture = texture,
                SelfModulate = color,
                PatchMarginLeft = ChatTheme.PatchH,
                PatchMarginRight = ChatTheme.PatchH,
                PatchMarginTop = ChatTheme.PatchV,
                PatchMarginBottom = ChatTheme.PatchV,
                AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Tile,
                AxisStretchVertical = NinePatchRect.AxisStretchMode.Tile,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            return patch;
        }

        private static TextureRect Tail(Color color, bool flip)
        {
            // dialogue_tail.png is 36x40; at the line's 28 width it is ~31 tall, pinned to the bubble's top.
            var tail = new TextureRect
            {
                Texture = ChatTheme.Tail,
                SelfModulate = color,
                FlipH = flip,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(ChatTheme.TailWidth, 31),
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = ChatTheme.Tail != null,
            };
            var shadow = new TextureRect
            {
                Texture = ChatTheme.Tail,
                SelfModulate = ChatTheme.ShadowColor,
                FlipH = flip,
                ShowBehindParent = true,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            shadow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            shadow.OffsetLeft = shadow.OffsetRight = 2;
            shadow.OffsetTop = shadow.OffsetBottom = 5;
            tail.AddChild(shadow);
            return tail;
        }
    }
}
