using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sts2AgentMcp.Chat;

internal sealed record AskOption(string Label, string Description);

/// <summary>One question of the AskUserQuestion tool (the same shape as Claude Code's own tool of that name).</summary>
internal sealed record AskQuestion(string Question, string Header, IReadOnlyList<AskOption> Options, bool MultiSelect);

/// <summary>
/// Chat panel: the agent's questions (docs/chat-roadmap.md step 3) as a card under its message, laid out like
/// Estelle's question view (packages/client/.../QuestionRequest.tsx): one tab per question (a mark on the answered
/// ones), the current question, its options as boxes (label and description together; the whole box is pressed),
/// and a field for the player's own answer. Picking an option of a single-choice question, or entering an own answer,
/// moves on to the next unanswered tab. [보내기] works once every question is answered. Then the card turns into the
/// answered view: every question with its answer, no tabs. The answer goes to the agent as the player's message
/// (one line per question: "header: choice"), which the chat does not show (the card shows it).
/// </summary>
internal sealed partial class ChatPanel
{
    private const int QuestionFont = 20;
    private const int OptionNoteFont = 17;

    /// <param name="onSubmit">Gets the message for the agent and the answer of each question; null when the card cannot
    /// be answered any more (a transcript drawn again).</param>
    /// <param name="answers">The answers given before: the card shows the answered view.</param>
    /// <returns>Closes the card with the given answers (the player went on in the game instead).</returns>
    public Action<IReadOnlyList<string>> AddQuestions(IReadOnlyList<AskQuestion> questions,
        Action<string, IReadOnlyList<string>>? onSubmit, IReadOnlyList<string>? answers)
    {
        var view = new QuestionCard(questions, onSubmit, answers);
        Append(Indented(view.Root));
        return view.Close;
    }

    private sealed class QuestionCard
    {
        private static readonly Color OptionColor = new(0.15f, 0.18f, 0.22f, 1f);
        private static readonly Color OptionHover = new(0.21f, 0.25f, 0.30f, 1f);
        private static readonly Color OptionBorder = new(0.32f, 0.36f, 0.42f, 1f);

        private readonly IReadOnlyList<AskQuestion> _questions;
        private readonly Action<string, IReadOnlyList<string>>? _onSubmit;
        private readonly List<string>[] _picked;
        private readonly string?[] _custom;
        private readonly VBoxContainer _body;
        private HFlowContainer _tabs = null!;
        private Label _question = null!;
        private VBoxContainer _options = null!;
        private HFlowContainer _customPills = null!;
        private LineEdit _field = null!;
        private Button _send = null!;
        private Label _hint = null!;
        private int _active;

        public QuestionCard(IReadOnlyList<AskQuestion> questions, Action<string, IReadOnlyList<string>>? onSubmit,
            IReadOnlyList<string>? answers)
        {
            _questions = questions;
            _onSubmit = onSubmit;
            _picked = questions.Select(_ => new List<string>()).ToArray();
            _custom = new string?[questions.Count];

            Root = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Root.AddThemeStyleboxOverride("panel", ChatTheme.CardStyle());
            _body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _body.AddThemeConstantOverride("separation", 10);
            Root.AddChild(_body);

            if (answers != null) BuildAnswered(answers);
            else if (onSubmit == null) BuildAnswered(questions.Select(_ => "(답하지 않음)").ToList());
            else BuildAsking();
        }

        public PanelContainer Root { get; }

        // ---- asking ----

        private void BuildAsking()
        {
            _tabs = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _tabs.AddThemeConstantOverride("h_separation", 6);
            _tabs.AddThemeConstantOverride("v_separation", 6);
            _body.AddChild(_tabs);

            _question = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            ChatTheme.StyleText(_question, QuestionFont);
            _body.AddChild(_question);

            _options = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _options.AddThemeConstantOverride("separation", 8);
            _body.AddChild(_options);

            _customPills = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _customPills.AddThemeConstantOverride("h_separation", 6);
            _body.AddChild(_customPills);

            _field = new LineEdit
            {
                PlaceholderText = "직접 입력 (Enter)",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 40),
            };
            ChatTheme.StyleText(_field, 18);
            _field.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, 0.35f));
            _field.AddThemeStyleboxOverride("normal", ChatTheme.InputStyle(false));
            _field.AddThemeStyleboxOverride("focus", ChatTheme.InputStyle(true));
            _field.TextSubmitted += _ => CommitCustom();
            _body.AddChild(_field);

            var bottom = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            bottom.AddThemeConstantOverride("separation", 8);
            _hint = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            ChatTheme.StyleText(_hint, OptionNoteFont, ChatTheme.DimColor);
            _send = new Button { Text = "보내기", CustomMinimumSize = new Vector2(96, 44) };
            ChatTheme.StyleButton(_send, ChatTheme.ClaudeColor, QuestionFont);
            _send.Pressed += Submit;
            bottom.AddChild(_hint);
            bottom.AddChild(_send);
            _body.AddChild(bottom);

            Render();
        }

        private bool Answered(int i) => _picked[i].Count > 0 || !string.IsNullOrEmpty(_custom[i]);

        private void Render()
        {
            var q = _questions[_active];

            foreach (var child in _tabs.GetChildren()) child.QueueFree();
            for (int i = 0; i < _questions.Count; i++)
            {
                int index = i;
                var tab = new Button { Text = (Answered(i) ? "● " : "") + _questions[i].Header, CustomMinimumSize = new Vector2(0, 38) };
                ChatTheme.StyleButton(tab, i == _active ? ChatTheme.PrimaryButton : ChatTheme.PlainButton, 18);
                if (Answered(i) && i != _active)
                    foreach (var key in new[] { "font_color", "font_hover_color" })
                        tab.AddThemeColorOverride(key, ChatTheme.OkColor);
                tab.Pressed += () => { _active = index; Render(); };
                _tabs.AddChild(tab);
            }

            _question.Text = ChatTheme.KeepAll(q.Question + (q.MultiSelect ? "  (여러 개 선택)" : ""));

            foreach (var child in _options.GetChildren()) child.QueueFree();
            foreach (var option in q.Options)
                _options.AddChild(OptionBox(option, _picked[_active].Contains(option.Label)));

            foreach (var child in _customPills.GetChildren()) child.QueueFree();
            if (!string.IsNullOrEmpty(_custom[_active]))
            {
                var pill = new Button { Text = _custom[_active] + "  ×", CustomMinimumSize = new Vector2(0, 38) };
                ChatTheme.StyleButton(pill, ChatTheme.ClaudeColor, 18);
                pill.Pressed += () => { _custom[_active] = null; Render(); };
                _customPills.AddChild(pill);
            }

            bool all = Enumerable.Range(0, _questions.Count).All(Answered);
            _send.Disabled = !all;
            _hint.Text = all ? "" : "모든 질문에 답하면 보낼 수 있어요.";
        }

        /// <summary>One option: label and description in one box; pressing anywhere on it picks it.</summary>
        private Control OptionBox(AskOption option, bool selected)
        {
            var box = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            var normal = OptionStyle(selected ? ChatTheme.ClaudeColor.Darkened(0.45f) : OptionColor, selected ? ChatTheme.ClaudeColor : OptionBorder);
            var hover = OptionStyle(selected ? ChatTheme.ClaudeColor.Darkened(0.35f) : OptionHover, selected ? ChatTheme.ClaudeColor : OptionBorder.Lightened(0.2f));
            box.AddThemeStyleboxOverride("panel", normal);
            box.MouseEntered += () => box.AddThemeStyleboxOverride("panel", hover);
            box.MouseExited += () => box.AddThemeStyleboxOverride("panel", normal);
            box.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                {
                    box.AcceptEvent();
                    Pick(option.Label);
                }
            };

            var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            lines.AddThemeConstantOverride("separation", 2);
            var label = new Label { Text = ChatTheme.KeepAll(option.Label), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            ChatTheme.StyleText(label, QuestionFont);
            if (ChatTheme.Bold != null) label.AddThemeFontOverride("font", ChatTheme.Bold);
            lines.AddChild(label);
            if (option.Description.Length > 0)
            {
                var note = new Label { Text = ChatTheme.KeepAll(option.Description), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                ChatTheme.StyleText(note, OptionNoteFont, selected ? ChatTheme.TextColor : ChatTheme.DimColor);
                lines.AddChild(note);
            }
            box.AddChild(lines);
            return box;
        }

        private static StyleBoxFlat OptionStyle(Color bg, Color border) => new()
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 10,
        };

        private void Pick(string label)
        {
            var list = _picked[_active];
            if (_questions[_active].MultiSelect)
            {
                if (!list.Remove(label)) list.Add(label);
                Render();
                return;
            }
            list.Clear();
            list.Add(label);
            _custom[_active] = null;
            NextUnanswered();
            Render();
        }

        /// <summary>Enter in the field: the text becomes this question's own answer, then the next question.</summary>
        private void CommitCustom()
        {
            string value = _field.Text.Trim();
            if (value.Length == 0)
            {
                // Enter on an empty field sends once everything is answered.
                if (!_send.Disabled) Submit();
                return;
            }
            _custom[_active] = value;
            if (!_questions[_active].MultiSelect) _picked[_active].Clear();
            _field.Clear();
            NextUnanswered();
            Render();
            _field.GrabFocus();
        }

        private void NextUnanswered()
        {
            for (int step = 1; step <= _questions.Count; step++)
            {
                int i = (_active + step) % _questions.Count;
                if (!Answered(i))
                {
                    _active = i;
                    return;
                }
            }
        }

        private void Submit()
        {
            if (_onSubmit == null || !Enumerable.Range(0, _questions.Count).All(Answered)) return;
            var answers = new List<string>();
            for (int i = 0; i < _questions.Count; i++)
            {
                var values = _picked[i].ToList();
                if (!string.IsNullOrEmpty(_custom[i])) values.Add(_custom[i]!);
                answers.Add(string.Join(", ", values));
            }
            string message = string.Join("\n", _questions.Select((q, i) => $"{q.Header}: {answers[i]}"));
            _closed = true;
            foreach (var child in _body.GetChildren()) child.QueueFree();
            BuildAnswered(answers);
            _onSubmit(message, answers);
        }

        /// <summary>Shows the answered view with these answers, if still asking.</summary>
        public void Close(IReadOnlyList<string> answers)
        {
            if (!GodotObject.IsInstanceValid(Root) || _closed) return;
            _closed = true;
            foreach (var child in _body.GetChildren()) child.QueueFree();
            BuildAnswered(answers);
        }

        private bool _closed;

        // ---- answered ----

        /// <summary>Every question with its answer, one under another (no tabs, nothing to press).</summary>
        private void BuildAnswered(IReadOnlyList<string> answers)
        {
            for (int i = 0; i < _questions.Count; i++)
            {
                var q = _questions[i];
                if (i > 0) _body.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });
                var header = new Label { Text = q.Header, MouseFilter = Control.MouseFilterEnum.Ignore };
                ChatTheme.StyleText(header, ChatTheme.SmallFontSize, ChatTheme.WaitColor);
                if (ChatTheme.Bold != null) header.AddThemeFontOverride("font", ChatTheme.Bold);
                _body.AddChild(header);
                var question = new Label { Text = ChatTheme.KeepAll(q.Question), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                ChatTheme.StyleText(question, OptionNoteFont, ChatTheme.DimColor);
                _body.AddChild(question);

                string answer = i < answers.Count ? answers[i] : "";
                var chosen = q.Options.FirstOrDefault(o => o.Label == answer);
                var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                box.AddThemeStyleboxOverride("panel", OptionStyle(ChatTheme.ClaudeColor.Darkened(0.45f), ChatTheme.ClaudeColor));
                var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                lines.AddThemeConstantOverride("separation", 2);
                var label = new Label { Text = ChatTheme.KeepAll(answer), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                ChatTheme.StyleText(label, QuestionFont);
                if (ChatTheme.Bold != null) label.AddThemeFontOverride("font", ChatTheme.Bold);
                lines.AddChild(label);
                if (chosen != null && chosen.Description.Length > 0)
                {
                    var note = new Label { Text = ChatTheme.KeepAll(chosen.Description), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                    ChatTheme.StyleText(note, OptionNoteFont);
                    lines.AddChild(note);
                }
                box.AddChild(lines);
                _body.AddChild(box);
            }
        }
    }
}
