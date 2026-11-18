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

    // Login card (docs/in-game-chat.md §3.1) and the header's logout button.
    private const float LogoutWidth = 132;
    // Start area: [Claude로 시작하기] / [Codex로 시작하기]; pressing one opens its card in its place (one at a time).
    private readonly VBoxContainer _start;
    private readonly Button _claudeStart;
    private readonly Button _codexStart;
    private readonly PanelContainer _codexCard;
    private string? _open; // null, "claude", "codex"
    private readonly PanelContainer _card;
    private readonly Label _cardPill;
    private readonly Dictionary<string, Label> _cardValues = new();
    private readonly Label _cardStatus;
    private readonly Label _cardNote;
    private readonly CheckBox _autoConnect;
    private readonly HBoxContainer _cardButtons;
    private readonly Button _cardPrimary;
    private readonly Button _cardSecondary;
    private readonly Button _logout;
    private AuthState? _auth;
    private string? _settingUp;
    private bool _confirmInstall;
    private bool _confirmLogin;
    private bool _connected;
    private bool _confirmLogout;
    private int _logoutArm;
    private bool _cardShown;
    private bool _logoutShown;
    private bool _hasRoom = true;
    private (Transform2D Stretch, Vector2 Win, Rect2 Game)? _lastLayout;
    private bool _placingCard;

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

        _logout = new Button { Text = "로그아웃", Visible = false, ClipText = true };
        ChatTheme.StyleButton(_logout, ChatTheme.PlainButton, ChatTheme.SmallFontSize);
        _logout.Pressed += OnLogoutPressed;
        Layer.AddChild(_logout);

        // The start area sits over the message list, centered (placed by hand: see PlaceCard).
        _start = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _start.AddThemeConstantOverride("separation", 16);
        _claudeStart = new Button { Text = "Claude로 시작하기", CustomMinimumSize = new Vector2(0, 64) };
        ChatTheme.StyleButton(_claudeStart, ChatTheme.PrimaryButton, 24);
        _claudeStart.Pressed += () => Open("claude");
        _codexStart = new Button { Text = "Codex로 시작하기", CustomMinimumSize = new Vector2(0, 64) };
        ChatTheme.StyleButton(_codexStart, ChatTheme.PrimaryButton, 24);
        _codexStart.Pressed += () => Open("codex");

        _card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _card.AddThemeStyleboxOverride("panel", ChatTheme.CardStyle());
        var body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 14);
        _card.AddChild(body);

        // Title row: Claude로 시작하기 ............ (in-progress pill)
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 12);
        var title = CardTitle("Claude로 시작하기");
        _cardPill = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        ChatTheme.StyleText(_cardPill, ChatTheme.SmallFontSize);
        top.AddChild(title);
        top.AddChild(_cardPill);
        body.AddChild(top);

        var facts = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        facts.AddThemeConstantOverride("h_separation", 18);
        facts.AddThemeConstantOverride("v_separation", 6);
        foreach (var (key, label) in new[] { ("install", "설치"), ("account", "계정"), ("plan", "플랜") })
        {
            var name = new Label { Text = label, MouseFilter = Control.MouseFilterEnum.Ignore };
            ChatTheme.StyleText(name, 20, ChatTheme.DimColor);
            var value = new Label
            {
                Text = "…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            ChatTheme.StyleText(value, 20);
            facts.AddChild(name);
            facts.AddChild(value);
            _cardValues[key] = value;
        }
        body.AddChild(facts);

        // One status line (colored) and a guidance note under it.
        _cardStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        ChatTheme.StyleText(_cardStatus, 22);
        if (ChatTheme.Bold != null) _cardStatus.AddThemeFontOverride("font", ChatTheme.Bold);
        body.AddChild(_cardStatus);

        _cardNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        ChatTheme.StyleText(_cardNote, 19);
        body.AddChild(_cardNote);

        _autoConnect = new CheckBox { Text = "다음부터 자동으로 접속", Visible = false };
        ChatTheme.StyleText(_autoConnect, 19);
        body.AddChild(_autoConnect);

        _cardButtons = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _cardButtons.AddThemeConstantOverride("separation", 12);
        _cardPrimary = new Button { CustomMinimumSize = new Vector2(0, 56), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ChatTheme.StyleButton(_cardPrimary, ChatTheme.PrimaryButton);
        _cardPrimary.Pressed += OnCardPrimary;
        _cardSecondary = new Button { CustomMinimumSize = new Vector2(0, 56), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ChatTheme.StyleButton(_cardSecondary, ChatTheme.PlainButton);
        _cardSecondary.Pressed += OnCardSecondary;
        _cardButtons.AddChild(_cardPrimary);
        _cardButtons.AddChild(_cardSecondary);
        body.AddChild(_cardButtons);

        // Codex: not supported yet; its card only says so.
        _codexCard = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _codexCard.AddThemeStyleboxOverride("panel", ChatTheme.CardStyle());
        var codexBody = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        codexBody.AddThemeConstantOverride("separation", 14);
        codexBody.AddChild(CardTitle("Codex로 시작하기"));
        var codexStatus = new Label { Text = "Codex는 아직 지원하지 않습니다.", AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        ChatTheme.StyleText(codexStatus, 22, ChatTheme.ErrorColor);
        if (ChatTheme.Bold != null) codexStatus.AddThemeFontOverride("font", ChatTheme.Bold);
        codexBody.AddChild(codexStatus);
        _codexCard.AddChild(codexBody);

        _start.AddChild(_claudeStart);
        _start.AddChild(_card);
        _start.AddChild(_codexStart);
        _start.AddChild(_codexCard);
        Layer.AddChild(_start);
        _start.MinimumSizeChanged += PlaceCard;

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
    public event Action? LoginPressed;
    public event Action? InstallPressed;
    public event Action? RecheckPressed;
    public event Action? LogoutPressed;
    /// <summary>The player pressed [접속]; the flag is "connect automatically next time".</summary>
    public event Action<bool>? ConnectPressed;

    /// <summary>Whether the login card is up (development tool).</summary>
    public bool CardVisible => _cardShown;

    /// <summary>The card's buttons as shown (development tool): primary, secondary; null when hidden.</summary>
    public (string? Primary, string? Secondary) CardButtons =>
        (_cardButtons.Visible && _cardPrimary.Visible ? _cardPrimary.Text : null,
         _cardButtons.Visible && _cardSecondary.Visible ? _cardSecondary.Text : null);

    public string CardNote => (_cardStatus.Text + " " + _cardNote.Text).Trim();

    public string CardDebug =>
        $"start visible={_start.Visible} open={_open ?? "-"} rect={_start.GetRect()} min={_start.GetCombinedMinimumSize()} " +
        $"scroll pos={_scroll.Position} size={_scroll.Size} hasRoom={_hasRoom}";

    /// <summary>Presses a card or header button by its label (development tool). Returns whether one matched.</summary>
    public bool Press(string label)
    {
        foreach (var button in new Button[] { _claudeStart, _codexStart, _cardPrimary, _cardSecondary, _logout, _autoConnect })
        {
            if (!button.IsVisibleInTree() || button.Disabled || button.Text != label) continue;
            if (button is CheckBox box) box.ButtonPressed = !box.ButtonPressed;
            else button.EmitSignal(BaseButton.SignalName.Pressed);
            return true;
        }
        return false;
    }

    // ---- login card ----

    /// <summary>
    /// Shows how the chat can run. The card is up while it cannot (it stays up through a re-check once shown, but
    /// the first check at startup runs behind the chat without it). The header gets a logout button on a subscription.
    /// </summary>
    /// <param name="settingUp">"login" / "install" while a console window opened from the card is at work.</param>
    /// <param name="message">An error to show instead of the usual note.</param>
    public void ShowAuth(AuthState auth, string? settingUp, string? message = null)
    {
        _auth = auth;
        _settingUp = settingUp;
        // A check at startup (card not up yet) runs behind the chat; a re-check keeps the card up. The card stays up
        // until the player connects ([접속], or automatically when they chose so).
        if (!(auth.Checking && !_cardShown)) _cardShown = !(auth.Ready && _connected);
        if (auth.Ready) { _confirmInstall = false; _confirmLogin = false; }
        _logoutShown = auth.Ready && auth.Method == AuthMethod.Subscription;
        Input.Editable = !_cardShown;
        Input.PlaceholderText = _cardShown ? "연결한 뒤 쓸 수 있어요" : "메시지 입력 (Enter 전송, Esc 게임으로)";
        if (_cardShown) Input.ReleaseFocus();
        ResetLogout();
        // The header's text widths depend on the logout button.
        if (_lastLayout is { } l) Layout(l.Stretch, l.Win, l.Game);
        else UpdateVisibility();
        DrawCard(message);
        // Wrapped labels settle their height a frame later.
        Callable.From(PlaceCard).CallDeferred();
    }

    private static Label CardTitle(string text)
    {
        var title = new Label
        {
            Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center,
        };
        ChatTheme.StyleText(title, 26);
        if (ChatTheme.Bold != null) title.AddThemeFontOverride("font", ChatTheme.Bold);
        return title;
    }

    /// <summary>Opens one agent's card in place of its start button; the other one goes back to its button.</summary>
    private void Open(string? which)
    {
        _open = which;
        _confirmInstall = false;
        _confirmLogin = false;
        _claudeStart.Visible = _open != "claude";
        _card.Visible = _open == "claude";
        _codexStart.Visible = _open != "codex";
        _codexCard.Visible = _open == "codex";
        DrawCard(null);
        Callable.From(PlaceCard).CallDeferred();
    }

    /// <summary>Whether the player has connected (pressed [접속] or chose automatic connection). Redraws.</summary>
    public void SetConnected(bool connected)
    {
        if (_connected == connected) return;
        _connected = connected;
        if (_auth != null) ShowAuth(_auth, _settingUp);
    }

    public bool Connected => _connected;

    /// <summary>
    /// The card at the message list's width, as tall as its contents, centered in the list's area. Done by hand on
    /// every layout and content change: a container would keep the tallest size it ever had (wrapped labels are very
    /// tall while their width is still zero) and push the card off screen.
    /// </summary>
    private void PlaceCard()
    {
        if (_placingCard) return;
        _placingCard = true;
        try
        {
            var area = new Rect2(_scroll.Position, _scroll.Size);
            _start.Size = new Vector2(area.Size.X, 0); // clamps up to the minimum height for this width
            float h = _start.Size.Y;
            _start.Position = new Vector2(area.Position.X, area.Position.Y + Mathf.Max(0, (area.Size.Y - h) / 2));
        }
        finally { _placingCard = false; }
    }

    private void UpdateVisibility()
    {
        foreach (var c in new Control[] { _headerBg, _headerIcon, _name, _status, Input }) c.Visible = _hasRoom;
        _scroll.Visible = _hasRoom && !_cardShown;
        _start.Visible = _hasRoom && _cardShown;
        _logout.Visible = _hasRoom && _logoutShown;
    }

    // Status lines (docs/in-game-chat.md §3.1): red = cannot run yet, green = ready, yellow = billed per use.
    private const string NeedInstall = "Claude Agent를 설치해야 합니다.";
    private const string NeedLogin = "로그인이 필요합니다.";
    private const string ReadyText = "사용할 수 있습니다.";
    private const string ApiText = "API로 사용됩니다. 요금이 부과될 수 있습니다.";

    private const string InstallGuide =
        "Anthropic의 공식 Claude 앱(Claude Code)을 이 PC에 설치합니다. 공식 설치 프로그램을 쓰고, 사용자 폴더에 설치되어 " +
        "관리자 권한이 필요 없습니다. 설치가 끝나면 같은 창에서 로그인이 이어집니다.";
    private const string AccountGuide =
        "본인의 Claude 계정으로 로그인합니다. 대화에 쓰는 사용량과 요금은 본인 계정에서 차감됩니다. " +
        "로그인 정보는 이 게임 전용 폴더에만 저장되며, 모드는 계정 정보를 읽거나 보내지 않습니다.";
    private const string ConnectGuide =
        "본인의 Claude 계정으로 접속합니다. 대화에 쓰는 사용량과 요금은 본인 계정에서 차감됩니다.";
    private const string ApiGuide =
        "본인이 설정한 API 키(ANTHROPIC_API_KEY)로 접속합니다. 대화에 쓴 만큼 해당 API 계정에 요금이 부과됩니다.";

    private void DrawCard(string? message)
    {
        var auth = _auth;
        if (auth == null || !_cardShown) return;
        _autoConnect.Visible = false;

        if (auth.Checking)
        {
            Pill("확인 중", ChatTheme.WaitColor);
            foreach (var value in _cardValues.Values) { value.Text = "…"; value.Modulate = Colors.White; }
            Status("이 PC의 설치와 로그인을 확인하고 있습니다.", ChatTheme.DimColor);
            Note(null, false);
            Buttons(null, null);
            return;
        }

        Fact("install", auth.Installed ? (auth.Version != null ? "v" + auth.Version : "설치됨") : "없음", auth.Installed);
        Fact("account", auth.Account ?? "—", auth.Account != null);
        Fact("plan", auth.Plan ?? "—", auth.Plan != null && auth.Plan != "Free");

        string kind = auth.CanInstall ? "install" : auth.CanLogin ? "login" : "";
        // Problem beyond the plain "not logged in / not installed" (expired, Free plan, other provider, ...).
        string? detail = auth.Problem is { } p && p != "로그인이 필요해요." && !p.Contains("설치되어 있지 않아요") ? p : null;

        if (_settingUp != null)
        {
            Pill(_settingUp == "install" ? "설치 중" : "로그인 대기", ChatTheme.WaitColor);
            Status(_settingUp == "install" ? NeedInstall : NeedLogin, ChatTheme.ErrorColor);
            Note(message ?? (_settingUp == "install"
                ? "열린 창에서 설치가 진행됩니다. 끝나면 이어서 로그인이 열리니, 브라우저에서 로그인을 마쳐 주세요. 마치면 자동으로 확인합니다."
                : "열린 창과 브라우저에서 로그인을 마쳐 주세요. 마치면 자동으로 확인합니다."), message != null);
            Buttons(kind.Length > 0 ? "다시 열기" : null, "다시 확인");
            return;
        }
        Pill(null, default);

        if (auth.Ready)
        {
            bool api = auth.Method == AuthMethod.ApiKey;
            Status(api ? ApiText : ReadyText, api ? ChatTheme.WarnColor : ChatTheme.OkColor);
            Note(message ?? (api ? ApiGuide : ConnectGuide), message != null);
            _autoConnect.Visible = true;
            Buttons("접속", null);
            return;
        }

        if (kind == "install")
        {
            Status(NeedInstall, ChatTheme.ErrorColor);
            if (_confirmInstall) { Note(InstallGuide, false); Buttons("설치", "취소"); }
            else { Note(message ?? detail, message != null); Buttons("설치", "다시 확인"); }
            return;
        }
        if (kind == "login")
        {
            Status(NeedLogin, ChatTheme.ErrorColor);
            if (_confirmLogin) { Note(AccountGuide, false); Buttons("로그인", "취소"); }
            else { Note(message ?? detail, message != null); Buttons("로그인", "다시 확인"); }
            return;
        }
        Status(auth.Problem ?? "연결할 수 없습니다.", ChatTheme.ErrorColor);
        Note(message ?? "해결한 뒤 [다시 확인]을 눌러 주세요.", message != null);
        Buttons(null, "다시 확인");
    }

    private void Pill(string? text, Color color)
    {
        _cardPill.Visible = text != null;
        _cardPill.Text = text ?? "";
        if (text != null) _cardPill.AddThemeColorOverride("font_color", color);
    }

    private void Status(string text, Color color)
    {
        _cardStatus.Text = text;
        _cardStatus.AddThemeColorOverride("font_color", color);
    }

    private void Fact(string key, string text, bool ok)
    {
        _cardValues[key].Text = text;
        _cardValues[key].Modulate = ok ? Colors.White : new Color(1, 0.7f, 0.65f, 0.8f);
    }

    private void Note(string? text, bool error)
    {
        _cardNote.Visible = !string.IsNullOrEmpty(text);
        _cardNote.Text = text ?? "";
        if (error) _cardNote.AddThemeColorOverride("font_color", ChatTheme.ErrorColor);
        else _cardNote.AddThemeColorOverride("font_color", ChatTheme.TextColor);
    }

    private void Buttons(string? primary, string? secondary)
    {
        _cardButtons.Visible = primary != null || secondary != null;
        _cardPrimary.Visible = primary != null;
        _cardPrimary.Text = primary ?? "";
        _cardSecondary.Visible = secondary != null;
        _cardSecondary.Text = secondary ?? "";
    }

    private void OnCardPrimary()
    {
        var auth = _auth;
        if (auth == null || auth.Checking) return;
        switch (_cardPrimary.Text)
        {
            case "설치" when !_confirmInstall:
                _confirmInstall = true;
                DrawCard(null);
                break;
            case "설치":
                _confirmInstall = false;
                InstallPressed?.Invoke();
                break;
            case "로그인" when !_confirmLogin:
                _confirmLogin = true;
                DrawCard(null);
                break;
            case "로그인":
                _confirmLogin = false;
                LoginPressed?.Invoke();
                break;
            case "접속":
                bool remember = _autoConnect.ButtonPressed;
                SetConnected(true);
                ConnectPressed?.Invoke(remember);
                break;
            case "다시 열기":
                if (_settingUp == "install" || auth.CanInstall) InstallPressed?.Invoke();
                else LoginPressed?.Invoke();
                break;
        }
    }

    private void OnCardSecondary()
    {
        if (_cardSecondary.Text == "취소")
        {
            _confirmInstall = false;
            _confirmLogin = false;
            DrawCard(null);
            return;
        }
        RecheckPressed?.Invoke();
    }

    /// <summary>Logout asks once more on the button itself; the question goes away after a few seconds.</summary>
    private void OnLogoutPressed()
    {
        if (!_confirmLogout)
        {
            _confirmLogout = true;
            _logout.Text = "정말요?";
            ChatTheme.StyleButton(_logout, ChatTheme.DangerButton, ChatTheme.SmallFontSize);
            int arm = ++_logoutArm;
            Layer.GetTree().CreateTimer(4).Timeout += () => { if (arm == _logoutArm) ResetLogout(); };
            return;
        }
        ResetLogout();
        LogoutPressed?.Invoke();
    }

    private void ResetLogout()
    {
        _logoutArm++;
        if (!_confirmLogout && _logout.Text == "로그아웃") return;
        _confirmLogout = false;
        _logout.Text = "로그아웃";
        ChatTheme.StyleButton(_logout, ChatTheme.PlainButton, ChatTheme.SmallFontSize);
    }

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
        _lastLayout = (stretch, win, game);
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
        // Logout at the header's right; the name and status stop before it while it is shown.
        float logoutRoom = _logoutShown ? LogoutWidth + 12 : 0;
        _logout.Size = new Vector2(LogoutWidth, 48);
        _logout.Position = strip.Position + new Vector2(strip.Size.X - DividerHit - Pad - LogoutWidth, (HeaderHeight - 48) / 2);
        _name.Size = new Vector2(Mathf.Max(0, strip.Size.X - textX - Pad - DividerHit - logoutRoom), 36);
        _status.Position = strip.Position + new Vector2(textX, 48);
        _status.Size = new Vector2(Mathf.Max(0, strip.Size.X - textX - Pad - DividerHit - logoutRoom), 26);

        _scroll.Position = strip.Position + new Vector2(Pad, HeaderHeight + 12);
        _scroll.Size = new Vector2(inner, Mathf.Max(0, strip.Size.Y - HeaderHeight - 12 - InputHeight - 2 * Pad));
        PlaceCard();

        Input.Position = strip.Position + new Vector2(Pad, strip.Size.Y - InputHeight - Pad);
        Input.Size = new Vector2(inner, InputHeight);

        _hasRoom = strip.Size.X >= 160;
        UpdateVisibility();

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
