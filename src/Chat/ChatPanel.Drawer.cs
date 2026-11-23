using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Chat;

/// <summary>One step of a plan: a card or a potion (with its target), or a short text (ending the turn is one).</summary>
/// <param name="Ref">The card's or potion's id on screen (c3, p0) for the hint arrows, when it has one.</param>
/// <param name="Note">What the step does, very short (-6, 처치, 방어 5), shown under it.</param>
internal sealed record DrawerStep(CardModel? Card, PotionModel? Potion, string? Ref, string? Target, string? TargetName, string? Text,
    string? Note = null);

/// <summary>A plan the player can pick: a label in its ink color, an optional name, steps in order, a one-line result.</summary>
internal sealed record DrawerPlan(string Label, string? Name, bool Recommended, IReadOnlyList<DrawerStep> Steps, string? Summary,
    string Click, IReadOnlyList<string> Actions, Color Color);

/// <summary>A card shown side by side with others (rewards, shop, upgrades), with a short caption.</summary>
internal sealed record DrawerCard(CardModel Card, string? Caption);

internal sealed record DrawerContent(string? Title, IReadOnlyList<DrawerPlan> Plans, IReadOnlyList<DrawerCard> Cards, IReadOnlyList<string> Lines);

/// <summary>
/// Chat panel: the drawer. A panel that slides out of the chat's right edge over the game (the agent's <c>mark</c>
/// combat_plan and overlay). Plans (combat_plan, three at most) are rows of the same height and width: a band in the plan's ink color with its
/// label (and 추천 under it), the plan's name, its steps (small game cards, potions, short texts) with the target under
/// each, a result line, and a select button on the right. Steps that do not fit scroll sideways (swipe, wheel or the
/// bar). The drawer is as wide as its widest plan, up to 80% of the game's width. Hovering a card or potion shows the
/// game's tooltip, hovering a plan draws its arrows on the game (<see cref="ScreenMarks.ShowHint"/>), the select button
/// raises <see cref="PlanPressed"/>. It opens by itself when the agent fills it; the player closes it (a small tab stays
/// on the edge to open it again, with a dot when something new came in). It is emptied when the turn or the room changes.
/// </summary>
internal sealed partial class ChatPanel
{
    // The drawer is drawn at 1.5x the chat's own scale so its text reads over the game.
    private const float Z = 1.5f;
    private static int F(int size) => (int)Mathf.Round(size * Z);

    public const int MaxPlans = 3;
    private const float DefaultDrawerWidth = 360 * Z;   // without plans (cards, lines)
    private const float MinDrawerWidth = 240 * Z;
    private const float MaxDrawerShare = 0.8f;          // of the game's width
    private const float DrawerMargin = 12 * Z;
    private const float DrawerTop = 80;     // below the game's top bar
    private const float DrawerBottomGap = 12;
    private const float MiniCardWidth = 66 * Z;
    private static float MiniCardHeight => MiniCardWidth * MegaCrit.Sts2.Core.Nodes.Cards.NCard.defaultSize.Y / MegaCrit.Sts2.Core.Nodes.Cards.NCard.defaultSize.X;
    private const float BandWidth = 36 * Z;
    private const float SelectWidth = 44 * Z;
    private const float DrawerTabTop = 140;
    private static readonly Vector2 DrawerTabSize = new(24, 64);
    private const double SlideSeconds = 0.18;

    private Control _drawerClip = null!;
    private PanelContainer _drawer = null!;
    private Label _drawerTitle = null!;
    private ScrollContainer _drawerScroll = null!;
    private VBoxContainer _drawerBody = null!;
    private Panel _drawerTab = null!;
    private Panel _drawerDot = null!;
    private Tween? _drawerTween;
    private DrawerContent? _drawerContent;
    private bool _drawerOpen;
    private float _drawerWidth = DefaultDrawerWidth;
    /// <summary>Each plan box with its steps row (the row scrolls, so the box's own minimum width leaves it out).</summary>
    private readonly List<(Control Box, Control Steps)> _planRows = new();

    /// <summary>A plan was picked: its label, "answer" or "operate", and its actions.</summary>
    public event Action<string, string, IReadOnlyList<string>>? PlanPressed;

    private void BuildDrawer()
    {
        // The clip keeps the closed drawer hidden behind the chat's edge; it passes clicks through.
        _drawerClip = new Control { ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        Layer.AddChild(_drawerClip);

        var style = new StyleBoxFlat { BgColor = new Color(ChatTheme.PanelColor, 0.94f), BorderColor = LineIdle };
        style.SetBorderWidthAll(2);
        style.BorderWidthLeft = 0;
        style.CornerRadiusTopRight = style.CornerRadiusBottomRight = 12;
        style.SetContentMarginAll(DrawerMargin);
        _drawer = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Size = new Vector2(_drawerWidth, 120) };
        _drawer.AddThemeStyleboxOverride("panel", style);
        _drawerClip.AddChild(_drawer);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", F(8));
        _drawer.AddChild(column);

        var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _drawerTitle = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        ChatTheme.StyleText(_drawerTitle, F(18));
        if (ChatTheme.Bold != null) _drawerTitle.AddThemeFontOverride("font", ChatTheme.Bold);
        header.AddChild(_drawerTitle);
        var close = new Button { CustomMinimumSize = new Vector2(28, 28) * Z, FocusMode = Control.FocusModeEnum.None };
        ChatTheme.StyleButton(close, ChatTheme.PlainButton, ChatTheme.SmallFontSize);
        close.AddChild(Chevron(new Vector2(14, 14) * Z, left: true));
        close.Pressed += () => SetDrawerOpen(false);
        header.AddChild(close);
        column.AddChild(header);

        _drawerScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = Control.MouseFilterEnum.Pass };
        _drawerBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        _drawerBody.AddThemeConstantOverride("separation", F(6));
        _drawerScroll.AddChild(_drawerBody);
        // Rows settle their height after layout; the drawer follows.
        _drawerBody.MinimumSizeChanged += () => QueueFit();
        column.AddChild(_drawerScroll);

        // The tab left on the edge while the drawer is closed.
        var tabStyle = new StyleBoxFlat { BgColor = ChatTheme.HeaderColor, BorderColor = LineIdle };
        tabStyle.SetBorderWidthAll(2);
        tabStyle.BorderWidthLeft = 0;
        tabStyle.CornerRadiusTopRight = tabStyle.CornerRadiusBottomRight = 8;
        _drawerTab = new Panel
        {
            Size = DrawerTabSize, Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        _drawerTab.AddThemeStyleboxOverride("panel", tabStyle);
        _drawerTab.AddChild(Chevron(DrawerTabSize / 2, left: false));
        var dotStyle = new StyleBoxFlat { BgColor = Accent, BorderColor = ChatTheme.PanelColor };
        dotStyle.SetBorderWidthAll(2);
        dotStyle.SetCornerRadiusAll(6);
        _drawerDot = new Panel { Size = new Vector2(12, 12), Position = new Vector2(DrawerTabSize.X - 10, -4), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _drawerDot.AddThemeStyleboxOverride("panel", dotStyle);
        _drawerTab.AddChild(_drawerDot);
        _drawerTab.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
            _drawerTab.AcceptEvent();
            SetDrawerOpen(true);
        };
        _drawerTab.MouseEntered += () => tabStyle.BorderColor = Accent;
        _drawerTab.MouseExited += () => tabStyle.BorderColor = LineIdle;
        Layer.AddChild(_drawerTab);
    }

    private static Line2D Chevron(Vector2 center, bool left)
    {
        float d = left ? 1 : -1;
        return new Line2D
        {
            Points = new[] { center + new Vector2(4 * d, -7), center + new Vector2(-4 * d, 0), center + new Vector2(4 * d, 7) },
            Width = 3,
            DefaultColor = ChatTheme.TextColor,
            JointMode = Line2D.LineJointMode.Round,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        };
    }

    /// <summary>Fills the drawer and opens it (the agent's combat_plan or overlay).</summary>
    public void ShowDrawer(DrawerContent content)
    {
        _drawerContent = content;
        _planRows.Clear();
        foreach (var child in _drawerBody.GetChildren()) child.QueueFree();
        _drawerTitle.Text = content.Title ?? "";
        // Plans line up: all get a name row when one has a name.
        bool names = content.Plans.Any(p => !string.IsNullOrEmpty(p.Name));
        bool notes = content.Plans.Any(p => p.Steps.Any(st => !string.IsNullOrEmpty(st.Note)));
        foreach (var plan in content.Plans) _drawerBody.AddChild(PlanRow(plan, names, notes));
        if (content.Cards.Count > 0) _drawerBody.AddChild(CardsRow(content.Cards));
        foreach (var line in content.Lines)
        {
            var label = new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            ChatTheme.StyleText(label, F(16));
            _drawerBody.AddChild(label);
        }
        QueueFit();
        SetDrawerOpen(true);
    }

    /// <summary>Empties and hides the drawer (turn or room changed, or the agent cleared its marks).</summary>
    public void ClearDrawer()
    {
        if (_drawerContent == null) return;
        _drawerContent = null;
        _planRows.Clear();
        ScreenMarks.ClearHint();
        HideTip();
        foreach (var child in _drawerBody.GetChildren()) child.QueueFree();
        _drawerDot.Visible = false;
        SetDrawerOpen(false);
    }

    private void SetDrawerOpen(bool open)
    {
        _drawerOpen = open && _drawerContent != null;
        if (_drawerOpen) _drawerDot.Visible = false;
        else if (open) return;
        if (!_drawerOpen) { ScreenMarks.ClearHint(); HideTip(); }
        _drawerTween?.Kill();
        _drawerClip.Visible = true;
        _drawerTween = _drawer.CreateTween();
        _drawerTween.TweenProperty(_drawer, "position:x", _drawerOpen ? 0 : ClosedX, SlideSeconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(_drawerOpen ? Tween.EaseType.Out : Tween.EaseType.In);
        _drawerTween.TweenCallback(Callable.From(() =>
        {
            if (!_drawerOpen) _drawerClip.Visible = false;
            UpdateDrawerTab();
        }));
        UpdateDrawerTab();
    }

    private float ClosedX => -(_drawerWidth + 8);

    private void UpdateDrawerTab() => _drawerTab.Visible = _drawerContent != null && !_drawerOpen;

    /// <summary>The game's width in panel units (the drawer may take 80% of it).</summary>
    private float GameWidth() =>
        _lastLayout is { } l && l.Win.Y > 0 ? l.Game.Size.X / (l.Win.Y / LayerHeight) : 1415;

    private bool _fitQueued;
    private DrawerContent? _fitWidthFor;

    /// <summary>
    /// Fits the drawer on the next frame, once however often it is asked. Fitting changes sizes, which wrapped text
    /// answers by changing its height; a deferred call would run again in the same flush and could loop forever.
    /// </summary>
    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        ((SceneTree)Engine.GetMainLoop()).Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
        {
            _fitQueued = false;
            FitDrawer();
        }), (uint)GodotObject.ConnectFlags.OneShot);
    }

    /// <summary>
    /// The drawer's width follows its widest plan (up to 80% of the game, then the steps scroll) and its height its
    /// content (up to the bottom of the screen, then it scrolls).
    /// </summary>
    private void FitDrawer()
    {
        if (!GodotObject.IsInstanceValid(_drawer)) return;
        float width = DefaultDrawerWidth;
        if (_planRows.Count > 0)
        {
            float widest = _planRows.Where(r => GodotObject.IsInstanceValid(r.Box))
                .Select(r => r.Box.GetCombinedMinimumSize().X + r.Steps.GetCombinedMinimumSize().X).DefaultIfEmpty(0).Max();
            width = Mathf.Clamp(widest + 2 * DrawerMargin, MinDrawerWidth, Mathf.Max(MinDrawerWidth, GameWidth() * MaxDrawerShare));
        }
        // Plans keep their width once shown (wrapped text must not make it creep).
        if (_planRows.Count > 0 && _fitWidthFor == _drawerContent && width < _drawerWidth) width = _drawerWidth;
        _fitWidthFor = _drawerContent;
        _drawerWidth = width;
        float max = LayerHeight - DrawerTop - DrawerBottomGap;
        float header = 28 * Z + F(8) + 2 * DrawerMargin; // header row, separation, content margins
        float body = Mathf.Min(_drawerBody.GetCombinedMinimumSize().Y, max - header);
        if (!Mathf.IsEqualApprox(_drawerScroll.CustomMinimumSize.Y, body)) _drawerScroll.CustomMinimumSize = new Vector2(0, body);
        _drawer.Size = new Vector2(width, body + header);
        _drawerClip.Size = new Vector2(width + 8, LayerHeight - DrawerTop);
        if (_drawerTween == null || !_drawerTween.IsRunning()) _drawer.Position = new Vector2(_drawerOpen ? 0 : ClosedX, 0);
    }

    /// <param name="strip">The chat's area in panel units (empty while folded).</param>
    private void LayoutDrawer(Rect2 strip)
    {
        float x = strip.End.X;
        _drawerClip.Position = new Vector2(x, DrawerTop);
        _drawerClip.Size = new Vector2(_drawerWidth + 8, LayerHeight - DrawerTop);
        _drawerTab.Position = new Vector2(x, DrawerTabTop);
        if (_drawerTween == null || !_drawerTween.IsRunning())
            _drawer.Position = new Vector2(_drawerOpen ? 0 : ClosedX, 0);
        // The game's width changed (chat resized): the drawer's widest size changes with it.
        if (_drawerContent != null) QueueFit();
    }

    /// <summary>Right edge of the open drawer in panel units (tooltips go right of it), or null when closed.</summary>
    private float? DrawerRight() => _drawerOpen && _drawerClip.Visible ? _drawerClip.Position.X + _drawerWidth : null;

    // ---- plans ----

    /// <summary>The hovered plan stays lit, the others dim (null: none hovered, all lit).</summary>
    private void HighlightPlan(Control? hovered)
    {
        foreach (var (box, _) in _planRows)
            if (GodotObject.IsInstanceValid(box))
                box.Modulate = hovered == null || box == hovered ? Colors.White : new Color(0.5f, 0.5f, 0.55f);
    }

    /// <param name="nameRow">Give the plan a name bar (when any plan has a name).</param>
    /// <param name="noteRow">Give each step a note line under it (when any step has a note).</param>
    private Control PlanRow(DrawerPlan plan, bool nameRow, bool noteRow)
    {
        var style = new StyleBoxFlat { BgColor = ChatTheme.HeaderColor, BorderColor = LineIdle };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(F(8));
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", style);

        // The band on the left and the name bar on top are in the plan's ink color (an L); the rest is the body.
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 0);
        box.AddChild(row);
        row.AddChild(Band(plan, nameRow));
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        right.AddThemeConstantOverride("separation", 0);
        row.AddChild(right);
        if (nameRow) right.AddChild(NameBar(plan));
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 0);
        right.AddChild(body);

        var middle = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddThemeConstantOverride("margin_left", F(8));
        middle.AddThemeConstantOverride("margin_right", F(8));
        middle.AddThemeConstantOverride("margin_top", F(6));
        middle.AddThemeConstantOverride("margin_bottom", F(4));
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", F(2));
        middle.AddChild(column);
        var steps = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        steps.AddThemeConstantOverride("separation", F(4));
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            if (i > 0)
            {
                var arrow = SmallText("→", F(14), ChatTheme.DimColor);
                arrow.CustomMinimumSize = new Vector2(0, MiniCardHeight);
                arrow.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
                steps.AddChild(arrow);
            }
            var cell = StepView(plan.Steps[i], plan.Color);
            if (!noteRow) { steps.AddChild(cell); continue; }
            // The step's note under it, as wide as the step.
            var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            stack.AddThemeConstantOverride("separation", F(2));
            stack.AddChild(cell);
            var note = SmallText(plan.Steps[i].Note ?? "", F(15), ChatTheme.TextColor);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            note.ClipText = true;
            note.CustomMinimumSize = new Vector2(cell.CustomMinimumSize.X, 0);
            stack.AddChild(note);
            steps.AddChild(stack);
        }
        column.AddChild(SideScroll(steps, plan.Color));
        body.AddChild(middle);
        body.AddChild(ResultColumn(plan));
        body.AddChild(SelectButton(plan));

        var arrows = plan.Steps.Where(s => s.Ref != null && s.Target != null).Select(s => (s.Ref!, s.Target!)).ToList();
        box.MouseEntered += () =>
        {
            style.BorderColor = plan.Color.Lightened(0.25f);
            HighlightPlan(box);
            if (arrows.Count > 0) ScreenMarks.ShowHint(arrows, plan.Color);
        };
        box.MouseExited += () =>
        {
            // Entering a card inside the row also leaves the box; keep the hint while the mouse is still over it.
            if (box.GetGlobalRect().HasPoint(box.GetGlobalMousePosition())) return;
            style.BorderColor = LineIdle;
            HighlightPlan(null);
            ScreenMarks.ClearHint();
        };
        _planRows.Add((box, steps));
        return box;
    }

    private static Color InkFill(DrawerPlan plan) => plan.Color.Darkened(0.15f);

    private const float ResultWidth = 100 * Z;

    /// <summary>
    /// The plan's result on its right, past the steps: the summary's parts (split at " · " or new lines) one per line,
    /// under a small 결과 heading, set off from the steps by a thin line.
    /// </summary>
    private static Control ResultColumn(DrawerPlan plan)
    {
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.18f), BorderColor = new Color(ChatTheme.DimColor, 0.35f) };
        style.BorderWidthLeft = 1;
        style.ContentMarginLeft = F(8);
        style.ContentMarginRight = F(6);
        style.ContentMarginTop = F(6);
        style.ContentMarginBottom = F(6);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(ResultWidth, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", F(3));
        panel.AddChild(column);
        var parts = (plan.Summary ?? "").Split(new[] { " · ", "·", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return panel;
        var heading = SmallText("결과", F(12), ChatTheme.DimColor);
        column.AddChild(heading);
        foreach (var part in parts)
        {
            var line = SmallText(part, F(15), ChatTheme.TextColor);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(ResultWidth - F(14), 0);
            if (ChatTheme.Bold != null) line.AddThemeFontOverride("font", ChatTheme.Bold);
            column.AddChild(line);
        }
        return panel;
    }

    /// <summary>The plan's left band in its ink color: the label, and 추천 under it.</summary>
    private static Control Band(DrawerPlan plan, bool nameRow)
    {
        var style = new StyleBoxFlat { BgColor = InkFill(plan) };
        style.CornerRadiusTopLeft = style.CornerRadiusBottomLeft = F(8) - 2;
        var band = new PanelContainer { CustomMinimumSize = new Vector2(BandWidth, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        band.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", F(2));
        band.AddChild(column);
        var label = new Label { Text = plan.Label, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        ChatTheme.StyleText(label, F(24), Colors.White);
        if (ChatTheme.Bold != null) label.AddThemeFontOverride("font", ChatTheme.Bold);
        column.AddChild(label);
        if (plan.Recommended)
        {
            var tag = new Label { Text = "추천", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            ChatTheme.StyleText(tag, F(12), Colors.White);
            if (ChatTheme.Bold != null) tag.AddThemeFontOverride("font", ChatTheme.Bold);
            column.AddChild(tag);
        }
        return band;
    }

    /// <summary>The plan's name on a bar in its ink color across the top, joined to the band.</summary>
    private static Control NameBar(DrawerPlan plan)
    {
        var style = new StyleBoxFlat { BgColor = InkFill(plan) };
        style.CornerRadiusTopRight = F(8) - 2;
        style.ContentMarginLeft = F(8);
        style.ContentMarginRight = F(8);
        style.ContentMarginTop = F(3);
        style.ContentMarginBottom = F(4);
        var bar = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", style);
        var name = SmallText(plan.Name ?? "", F(17), Colors.White);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;
        if (ChatTheme.Bold != null) name.AddThemeFontOverride("font", ChatTheme.Bold);
        bar.AddChild(name);
        return bar;
    }

    /// <summary>The select button on the plan's right.</summary>
    private Control SelectButton(DrawerPlan plan)
    {
        var wrap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "right", "top", "bottom" }) wrap.AddThemeConstantOverride("margin_" + side, F(6));
        var button = new Button
        {
            Text = "선택",
            CustomMinimumSize = new Vector2(SelectWidth, 0),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        StyleBoxFlat Face(Color bg, Color border)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            s.SetBorderWidthAll(2);
            s.SetCornerRadiusAll(F(6));
            return s;
        }
        button.AddThemeStyleboxOverride("normal", Face(ChatTheme.PlainButton, plan.Color));
        button.AddThemeStyleboxOverride("hover", Face(plan.Color.Darkened(0.2f), plan.Color.Lightened(0.3f)));
        button.AddThemeStyleboxOverride("pressed", Face(plan.Color.Darkened(0.35f), plan.Color.Lightened(0.3f)));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        ChatTheme.StyleText(button, F(15));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        if (ChatTheme.Bold != null) button.AddThemeFontOverride("font", ChatTheme.Bold);
        button.Pressed += () => PlanPressed?.Invoke($"{plan.Label} 선택 — {Describe(plan)}", plan.Click, plan.Actions);
        wrap.AddChild(button);
        return wrap;
    }

    /// <summary>
    /// A row that scrolls sideways when wider than the plan: drag it (swipe) or turn the wheel over it. No bar: an
    /// arrow on the left or right edge shows there is more that way, and pressing it moves the row by about a card.
    /// Hovers pass through to the plan box.
    /// </summary>
    private static Control SideScroll(Control content, Color ink)
    {
        var scroll = new ScrollContainer
        {
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        scroll.AddChild(content);
        void Fit()
        {
            if (GodotObject.IsInstanceValid(scroll))
                scroll.CustomMinimumSize = new Vector2(0, content.GetCombinedMinimumSize().Y);
        }
        content.MinimumSizeChanged += Fit;
        Fit();

        // The arrows sit over the row's ends, as tall as a card.
        var stack = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.AddChild(scroll);
        var edges = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.AddChild(edges);
        Tween? slide = null;
        void Nudge(int direction)
        {
            var bar = scroll.GetHScrollBar();
            float to = Mathf.Clamp(scroll.ScrollHorizontal + direction * (MiniCardWidth + F(30)), 0, (float)(bar.MaxValue - bar.Page));
            slide?.Kill();
            slide = scroll.CreateTween();
            slide.TweenProperty(scroll, "scroll_horizontal", (int)to, 0.2).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        var left = EdgeArrow(left: true, ink, () => Nudge(-1));
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        var rightArrow = EdgeArrow(left: false, ink, () => Nudge(1));
        edges.AddChild(left);
        edges.AddChild(spacer);
        edges.AddChild(rightArrow);
        void Update()
        {
            if (!GodotObject.IsInstanceValid(scroll)) return;
            var bar = scroll.GetHScrollBar();
            left.Visible = scroll.ScrollHorizontal > 1;
            rightArrow.Visible = scroll.ScrollHorizontal < bar.MaxValue - bar.Page - 1;
        }
        scroll.GetHScrollBar().ValueChanged += _ => Update();
        scroll.GetHScrollBar().Changed += Update;
        scroll.Resized += Update;
        Callable.From(Update).CallDeferred();

        // Swipe: a left-button drag over the row moves it (the cards and tiles pass their input up to here).
        bool dragging = false;
        scroll.GuiInput += ev =>
        {
            switch (ev)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                    dragging = button.Pressed;
                    break;
                case InputEventMouseMotion motion when dragging && (motion.ButtonMask & MouseButtonMask.Left) != 0:
                    slide?.Kill();
                    scroll.ScrollHorizontal -= (int)Mathf.Round(motion.Relative.X);
                    scroll.AcceptEvent();
                    break;
            }
        };
        return stack;
    }

    /// <summary>A "more this way" arrow on a row's edge: a dark rounded strip with a chevron; pressing it scrolls.</summary>
    private static Control EdgeArrow(bool left, Color ink, Action pressed)
    {
        var size = new Vector2(18 * Z, MiniCardHeight);
        var idle = new Color(Colors.White, 0.7f);
        var style = new StyleBoxFlat { BgColor = new Color(ink.Darkened(0.1f), 0.92f), BorderColor = idle };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(F(6));
        var arrow = new Panel
        {
            CustomMinimumSize = size,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            Visible = false,
        };
        arrow.AddThemeStyleboxOverride("panel", style);
        var chevron = Chevron(Vector2.Zero, left);
        chevron.Position = size / 2;
        chevron.Scale = new Vector2(1.3f, 1.3f);
        chevron.DefaultColor = Colors.White;
        arrow.AddChild(chevron);
        arrow.MouseEntered += () => { style.BorderColor = Colors.White; style.BgColor = ink.Lightened(0.1f); };
        arrow.MouseExited += () => { style.BorderColor = idle; style.BgColor = new Color(ink.Darkened(0.1f), 0.92f); };
        arrow.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
            arrow.AcceptEvent();
            pressed();
        };
        return arrow;
    }

    /// <summary>The plan in words, sent with the pick so the agent knows what was chosen.</summary>
    private static string Describe(DrawerPlan plan) =>
        (plan.Name is { Length: > 0 } name ? $"[{name}] " : "") +
        string.Join(" → ", plan.Steps.Select(s =>
        {
            string? what = s.Card is { } card ? View.Fmt.CardName(card)
                : s.Potion is { } potion ? Text.Of(() => potion.Title) ?? "포션"
                : null;
            return what != null ? what + (s.TargetName != null ? $"({s.TargetName})" : "") : s.Text ?? "";
        }));

    /// <summary>
    /// A step: the tile (card, potion or text box, all one card tall) with its target as a badge on the tile's
    /// bottom-right corner, sticking out a little: the creature's live picture (its name on hover), or its name when
    /// there is no creature to show. Room for the overhang is kept either way so every plan is the same height.
    /// </summary>
    private Control StepView(DrawerStep step, Color ink)
    {
        Control tile = step.Card is { } card ? MiniCard(card, MiniCardWidth)
            : step.Potion is { } potion ? PotionTile(potion)
            : TextTile(step.Text ?? "");
        var tileSize = tile.CustomMinimumSize;
        float over = TargetSize * TargetOverhang;
        var cell = new Control { CustomMinimumSize = tileSize + new Vector2(over, over), MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        cell.AddChild(tile);
        if (step.TargetName != null)
        {
            Control badge;
            if (CreaturePortrait.Resolve(step.Target) is { } creature)
                badge = TargetFrame(CreaturePortrait.Create(creature, TargetSize - 2 * TargetRing), ink);
            else
            {
                var name = SmallText(step.TargetName, F(12), Colors.White);
                var pill = new StyleBoxFlat { BgColor = ink.Darkened(0.2f), BorderColor = Colors.White };
                pill.SetBorderWidthAll(2);
                pill.SetCornerRadiusAll(F(10));
                pill.SetContentMarginAll(F(3));
                badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
                badge.AddThemeStyleboxOverride("panel", pill);
                badge.AddChild(name);
            }
            var size = badge.GetCombinedMinimumSize();
            badge.Position = tileSize + new Vector2(over, over) - size;
            var entries = new List<(string Title, string Body)> { (step.TargetName, "") };
            badge.MouseEntered += () => ShowTip(0, entries, badge.GetGlobalMousePosition());
            badge.MouseExited += HideTip;
            cell.AddChild(badge);
        }
        return cell;
    }

    private const float TargetSize = 51 * Z;       // the target badge's diameter
    private const float TargetOverhang = 0.2f;     // how much of it sticks out past the tile
    private const float TargetRing = 3;

    /// <summary>A round frame in the plan's ink around a target's picture (clipped to a circle).</summary>
    private static Control TargetFrame(Control picture, Color ink)
    {
        // The clip's own alpha masks its children, so it is drawn opaque.
        var mask = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.08f, 1f) };
        mask.SetCornerRadiusAll((int)(TargetSize / 2));
        var clip = new Panel { CustomMinimumSize = new Vector2(TargetSize, TargetSize), ClipChildren = CanvasItem.ClipChildrenMode.AndDraw,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        clip.AddThemeStyleboxOverride("panel", mask);
        picture.Position = new Vector2(TargetRing, TargetRing);
        clip.AddChild(picture);
        var ring = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = ink.Lightened(0.15f) };
        ring.SetBorderWidthAll((int)TargetRing);
        ring.SetCornerRadiusAll((int)(TargetSize / 2));
        var border = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        border.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        border.AddThemeStyleboxOverride("panel", ring);
        var holder = new Control { CustomMinimumSize = new Vector2(TargetSize, TargetSize), MouseFilter = Control.MouseFilterEnum.Pass };
        holder.AddChild(clip);
        holder.AddChild(border);
        return holder;
    }

    /// <summary>A potion's picture in a card-sized slot, with the game's tooltip on hover.</summary>
    private Control PotionTile(PotionModel potion)
    {
        Texture2D? image = null;
        try { image = potion.Image; } catch (Exception ex) { Host.Log.Warn($"chat: potion image {potion.Id}: {ex.Message}"); }
        var slot = new TextureRect
        {
            Texture = image,
            CustomMinimumSize = new Vector2(MiniCardWidth, MiniCardHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        var gameTips = Markup.PotionGameTips(potion);
        var entries = Markup.PotionTip(potion);
        slot.MouseEntered += () =>
        {
            float y = slot.GetGlobalRect().Position.Y;
            var at = slot.GetGlobalMousePosition();
            if (!ShowGameTip(slot, y, gameTips, at)) ShowTip(y, entries, at);
        };
        slot.MouseExited += HideTip;
        return slot;
    }

    /// <summary>Text in the place of a card: one card tall, as wide as the text needs between one and two cards.</summary>
    private static Control TextTile(string text)
    {
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.22f), BorderColor = new Color(ChatTheme.DimColor, 0.35f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(F(6));
        style.SetContentMarginAll(F(6));
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ChatTheme.StyleText(label, F(15));
        float textWidth = label.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, F(15)).X;
        float width = Mathf.Clamp(textWidth + 2 * F(6) + 4, MiniCardWidth, 2 * MiniCardWidth);
        var tile = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, MiniCardHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tile.AddThemeStyleboxOverride("panel", style);
        tile.AddChild(label);
        return tile;
    }

    // ---- cards ----

    private Control CardsRow(IReadOnlyList<DrawerCard> cards)
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", F(8));
        flow.AddThemeConstantOverride("v_separation", F(8));
        foreach (var entry in cards)
        {
            var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(MiniCardWidth + 30 * Z, 0) };
            column.AddThemeConstantOverride("separation", F(2));
            column.AddChild(MiniCard(entry.Card, MiniCardWidth));
            if (!string.IsNullOrEmpty(entry.Caption))
            {
                var caption = SmallText(entry.Caption!, F(13), ChatTheme.DimColor);
                caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                caption.CustomMinimumSize = new Vector2(MiniCardWidth + 30 * Z, 0);
                column.AddChild(caption);
            }
            flow.AddChild(column);
        }
        return flow;
    }

    private Control MiniCard(CardModel card, float width)
    {
        float height = width * MegaCrit.Sts2.Core.Nodes.Cards.NCard.defaultSize.Y / MegaCrit.Sts2.Core.Nodes.Cards.NCard.defaultSize.X;
        var slot = new TextureRect
        {
            CustomMinimumSize = new Vector2(width, height),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        var entries = Markup.CardTip(card);
        var gameTips = Markup.CardGameTips(card);
        slot.MouseEntered += () =>
        {
            float y = slot.GetGlobalRect().Position.Y;
            var at = slot.GetGlobalMousePosition();
            if (!ShowGameTip(slot, y, gameTips, at)) ShowTip(y, entries, at);
        };
        slot.MouseExited += HideTip;
        RenderCard(card, slot);
        return slot;
    }

    private static Label SmallText(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        ChatTheme.StyleText(label, size, color);
        return label;
    }
}
