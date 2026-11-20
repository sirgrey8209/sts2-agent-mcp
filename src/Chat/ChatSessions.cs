using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// The chat's conversations (docs/chat-roadmap.md): one control session for the main menu (kept across game starts)
/// and one play session per run (a new conversation for every run, so a long evening of runs never piles up into one
/// compacted conversation). The chat panel shows the session that fits where the player is; each session keeps its
/// own transcript, shown again when the chat switches back to it.
///
/// What goes to a play session besides the player's words: the run brief before its first message (play level, the
/// control session's brief, the learning notes), the game log since the last message (<c>[게임 기록]</c>: the
/// player's actions and the views opened), automatic messages after important moments (<c>[자동]</c>, depending on
/// the play level), and the request for a run report at the game over screen. The report goes to the control
/// session with its next message. Main thread only.
/// </summary>
internal static class ChatSessions
{
    private const int MaxLogLines = 40;
    private static readonly TimeSpan AutoGap = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan TypingGrace = TimeSpan.FromSeconds(5);
    private const int MaxContinues = 400;

    private sealed record Item(string Kind, string Text, bool Error = false,
        IReadOnlyList<CardModel>? Cards = null, IReadOnlyList<RelicModel>? Relics = null, Markup? Markup = null,
        IReadOnlyList<AskQuestion>? Questions = null);

    private static Config _config = null!;
    private static ChatPanel? _panel;
    private static SessionStore _store = null!;
    private static AuthState? _auth;
    private static AgentSession? _control;
    private static AgentSession? _play;
    private static AgentSession? _active;
    private static readonly Dictionary<AgentSession, List<Item>> Transcripts = new();
    private static readonly HashSet<AgentSession> Briefed = new();
    private static readonly HashSet<string> ReportRequested = new();
    private static readonly List<string> Views = new();
    private static readonly List<string> Signals = new();
    private static long _logMark = -1;
    private static DateTime _lastAuto = DateTime.MinValue;
    private static DateTime _lastUserMessage = DateTime.MinValue;
    private static bool _autoPaused;
    private static int _continues;
    private static bool _levelChanged;
    /// <summary>The turn running now was started by the chat itself ([자동]), not by the player.</summary>
    private static bool _autoTurn;
    /// <summary>An outside MCP client took the game: the chat's agents stay stopped until the player writes again.</summary>
    private static bool _externalHold;
    /// <summary>The question card waiting for the player's answer (AskUserQuestion), if any.</summary>
    private static Item? _pendingQuestion;
    /// <summary>The agent waits for the player to pick something on the game screen (wait_for_player).</summary>
    private static bool _waitingForGame;
    /// <summary>Closes a waiting question card (the player went on in the game instead).</summary>
    private static Action<IReadOnlyList<string>>? _closeQuestion;
    /// <summary>Each answered question card's answers (one per question), to draw it again answered.</summary>
    private static readonly Dictionary<Item, IReadOnlyList<string>> Answers = new();
    private static bool _installed;

    /// <summary>Raised when a session's login stops working (the controller checks again).</summary>
    public static event Action<string>? AuthLost;

    public static PlayLevel Level => Prompts.ParseLevel(_config?.ChatPlayLevel) ?? PlayLevel.CoLow;

    public static ChatRole? ActiveRole => _active?.Role;

    public static void Attach(ChatPanel panel, Config config)
    {
        _panel = panel;
        if (_installed)
        {
            Replay();
            return;
        }
        _installed = true;
        _config = config;
        _store = SessionStore.Load();
        _logMark = EventLog.Mark();
        GameWatch.RunChanged += OnRunChanged;
        GameWatch.Signal += OnSignal;
        GameWatch.Install();
        MainThread.Every(TimeSpan.FromMilliseconds(500), TryAuto);
        // The player plays: card selections go to the agent only while it acts, and the game is covered meanwhile.
        ChoiceBroker.Instance.AlwaysIntercept = false;
        Tools.ExternalPlay.Started += OnExternalPlay;
        InputLock.Install();
        AgentLock.Changed += busy => _panel?.SetAgentBusy(busy);
        Activate(Control());
    }

    public static void SetAuth(AuthState state)
    {
        _auth = state;
        foreach (var session in AllSessions()) session.Auth = state;
    }

    /// <summary>The player sent a message.</summary>
    public static void UserMessage(string text) => SendPlayer(text, visible: true);

    /// <summary>The player answered a question card: the card shows the answers; the message itself is not shown.</summary>
    private static void Answered(Item question, string text, IReadOnlyList<string> answers)
    {
        Answers[question] = answers;
        SendPlayer(text, visible: false);
    }

    private static void SendPlayer(string text, bool visible)
    {
        _lastUserMessage = DateTime.UtcNow;
        _pendingQuestion = null;
        _closeQuestion = null;
        _waitingForGame = false;
        if (IsStopWord(text)) _autoPaused = true;
        else if (IsGoWord(text)) _autoPaused = false;
        _continues = 0;

        if (Tools.ExternalPlay.Running)
        {
            // An outside client is acting right now: not yet. The text goes back into the input box.
            const string wait = "외부 에이전트가 행동하는 중이에요. 끝나면 다시 보내 주세요.";
            if (_active != null) Record(_active, new Item("note", wait));
            _panel?.AddNote(wait);
            if (visible && _panel != null && _panel.Input.Text.Length == 0) _panel.Input.Text = text;
            return;
        }
        if (_externalHold)
        {
            _externalHold = false;
            const string back = "인게임 에이전트를 다시 이어서 해요.";
            if (_active != null) Record(_active, new Item("note", back));
            _panel?.AddNote(back);
        }
        var session = _active ?? Activate(Control());
        _autoTurn = false;
        if (visible)
        {
            Record(session, new Item("user", text));
            _panel?.AddUser(text);
        }
        session.Send(Compose(session, text));
    }

    /// <summary>After a logout: stop everything and forget the conversations.</summary>
    public static void StopAll(bool forget)
    {
        foreach (var session in AllSessions()) session.Stop(forget);
        if (forget) _store.ForgetSessions();
    }

    /// <summary>The play level changed (play_level tool): saved, and the play session runs on it from its next message.</summary>
    public static void SetLevel(PlayLevel level)
    {
        _config.SaveChatPlayLevel(Prompts.LevelKey(level));
        _levelChanged = true;
        _autoPaused = false;
        UpdateStatus();
    }

    public static string? Brief
    {
        get => _store.Brief;
        set => _store.Brief = value;
    }

    // ---- showing things (chat tools) ----

    public static void ShowCards(IReadOnlyList<CardModel> cards)
    {
        if (_active == null || cards.Count == 0) return;
        Record(_active, new Item("cards", "", Cards: cards));
        _panel?.AddCards(cards);
    }

    public static void ShowRelics(IReadOnlyList<RelicModel> relics)
    {
        if (_active == null || relics.Count == 0) return;
        Record(_active, new Item("relics", "", Relics: relics));
        _panel?.AddRelics(relics);
    }

    /// <summary>The agent's questions (AskUserQuestion) as a card; the player's answer comes back as their message.</summary>
    public static bool ShowQuestions(IReadOnlyList<AskQuestion> questions)
    {
        if (_active == null || _panel == null) return false;
        var item = new Item("question", "", Questions: questions);
        Record(_active, item);
        _pendingQuestion = item;
        _closeQuestion = _panel.AddQuestions(questions, (text, answers) => Answered(item, text, answers), null);
        return true;
    }

    /// <summary>The agent recommended a pick that is on the game screen and waits for the player to make it there
    /// (wait_for_player). When the game moves on, the agent is told and goes on (in the levels where it plays).</summary>
    public static bool WaitForPlayer()
    {
        if (_active?.Role != ChatRole.Play) return false;
        _waitingForGame = true;
        return true;
    }

    /// <summary>Something happened in the game while a question card or a wait was open: the player went on in the
    /// game. The card closes; a waiting agent is told.</summary>
    private static void PlayerMovedInGame(GameSignal signal)
    {
        if (_pendingQuestion is { } question)
        {
            var answers = question.Questions!.Select(_ => "(게임에서 직접 진행)").ToList();
            Answers[question] = answers;
            _closeQuestion?.Invoke(answers);
            _pendingQuestion = null;
            _closeQuestion = null;
        }
        if (!_waitingForGame) return;
        _waitingForGame = false;
        var session = _play;
        if (session == null || session != _active || !Connected || Level is not (PlayLevel.CoHigh or PlayLevel.Auto)) return;
        // Let the game settle on its next screen first.
        Task.Delay(1000).ContinueWith(_ => MainThread.Run(() =>
        {
            if (session != _active || !session.Idle || PlayerTyping() || _autoPaused) return;
            SendAuto(session, $"플레이어가 게임에서 직접 골랐다({signal.Text}). 이어서 진행해.");
        }));
    }

    // ---- sessions ----

    private static IEnumerable<AgentSession> AllSessions() =>
        new[] { _control, _play }.Where(s => s != null).Select(s => s!);

    private static AgentSession Control()
    {
        if (_control != null) return _control;
        var session = NewSession(ChatRole.Control, null, _store.ControlId);
        session.SessionIdChanged += id => MainThread.Run(() => _store.ControlId = id);
        _control = session;
        return session;
    }

    private static AgentSession Play(string runKey)
    {
        if (_play?.RunKey == runKey) return _play;
        string? resume = _store.PlayId(runKey);
        var session = NewSession(ChatRole.Play, runKey, resume);
        session.SessionIdChanged += id => MainThread.Run(() => _store.SetPlayId(runKey, id));
        // A resumed conversation already had its brief.
        if (resume != null) Briefed.Add(session);
        _play = session;
        return session;
    }

    private static AgentSession NewSession(ChatRole role, string? runKey, string? resume)
    {
        var session = new AgentSession(_config, role, runKey, resume, () => Prompts.System(role, Level));
        session.Event += ev => MainThread.Run(() => OnEvent(session, ev));
        session.Auth = _auth;
        // A resumed conversation shows again what was said before (read from Claude Code's own record of it).
        Transcripts[session] = resume != null ? LoadHistory(resume) : new List<Item>();
        return session;
    }

    private static AgentSession Activate(AgentSession session)
    {
        if (_active == session) return session;
        _active = session;
        Signals.Clear();
        Replay();
        string note = session.Role == ChatRole.Control
            ? "메인 메뉴 · 관제 세션"
            : $"런 · 플레이 세션 · {Prompts.LevelName(Level)}";
        _panel?.AddNote(note);
        UpdateStatus();
        return session;
    }

    private static void Replay()
    {
        if (_panel == null || _active == null) return;
        _panel.ClearMessages();
        foreach (var item in Transcripts[_active]) Show(item, live: false);
        _panel.ShowTyping(!_active.Idle);
    }

    private static void Record(AgentSession session, Item item)
    {
        if (Transcripts.TryGetValue(session, out var list)) list.Add(item);
    }

    private static void Show(Item item, bool live)
    {
        if (_panel == null) return;
        switch (item.Kind)
        {
            case "user": _panel.AddUser(item.Text); break;
            case "agent": _panel.AddAgent(item.Markup ?? Markup.Parse(item.Text)); break;
            case "note": _panel.AddNote(item.Text, item.Error); break;
            case "cards": _panel.AddCards(item.Cards!); break;
            case "relics": _panel.AddRelics(item.Relics!); break;
            case "question":
            {
                var close = _panel.AddQuestions(item.Questions!,
                    item == _pendingQuestion ? (text, answers) => Answered(item, text, answers) : null,
                    Answers.TryGetValue(item, out var given) ? given : null);
                // The card drawn again is the one to close when the player goes on in the game.
                if (item == _pendingQuestion) _closeQuestion = close;
                break;
            }
        }
    }

    private static void UpdateStatus()
    {
        if (_panel == null || _auth is not { Ready: true } || !_panel.Connected) return;
        _panel.SetStatus(StatusPrefix() + "메시지를 보내면 연결해요");
    }

    private static string StatusPrefix() => _active?.Role == ChatRole.Play
        ? $"플레이 · {Prompts.LevelName(Level)} · "
        : "관제 · ";

    // ---- run changes ----

    private static void OnRunChanged(string? old, string? key)
    {
        Log.Info($"chat: run {old ?? "-"} -> {key ?? "-"}");
        _continues = 0;
        _autoPaused = false;
        _logMark = EventLog.Mark();
        Views.Clear();

        if (old != null && _play?.RunKey == old)
        {
            var ended = _play;
            // A run left without the game over screen (abandoned, or quit to the menu to continue later): a run
            // still saved can be continued, so only an abandoned one would need a report; it is not asked here.
            if (!ReportRequested.Contains(old)) ended.Stop();
        }

        if (key != null)
        {
            var session = Play(key);
            Activate(session);
            // In the levels where the agent plays, it starts right away.
            if (Level is PlayLevel.CoHigh or PlayLevel.Auto && Connected) SendAuto(session, "런이 시작됐다. 진행해.");
        }
        else
        {
            Activate(Control());
        }
    }

    private static void OnSignal(GameSignal signal)
    {
        if (signal.Kind == "view")
        {
            Views.Add(signal.Text);
            return;
        }
        if (signal.Kind == "game_over")
        {
            RequestReport();
            return;
        }
        if (signal.Kind is "deck" or "room" or "log" or "combat_start" && (_pendingQuestion != null || _waitingForGame))
            PlayerMovedInGame(signal);
        if (StepBackForExternal()) return;
        if (signal.Kind == "room") OpenShopOnEntry();
        if (_active?.Role != ChatRole.Play) return;
        if (!Wants(Level, signal.Kind)) return;
        Signals.Add(signal.Text);
    }

    /// <summary>
    /// Walking into a shop opens the merchant's wares (the player would click the merchant first anyway). Waits for the
    /// room to be on screen; done once per entry; nothing when the wares are already open or the room is gone.
    /// </summary>
    private static void OpenShopOnEntry()
    {
        if (GameCtx.Capture().Run?.CurrentRoom is not MerchantRoom) return;
        int frames = 0;
        _ = MainThread.Poll(() =>
        {
            frames++;
            var room = NMerchantRoom.Instance;
            if (room == null || !Ui.IsVisible(room)) return frames > 600 ? "gone" : null;
            if (room.Inventory?.IsOpen == true) return "open";
            if (frames < 30) return null; // let the room finish coming in
            if (AgentLock.Held) return "agent"; // the agent is acting there itself
            room.OpenInventory();
            Log.Info("chat: shop opened on entry");
            return "opened";
        }, TimeSpan.FromSeconds(15), () => "timeout");
    }

    /// <summary>
    /// An outside MCP client started acting on the game: every chat agent stops at once (a running turn is cut, queued
    /// messages are dropped) and stays stopped until the player writes in the chat again (<see cref="SendPlayer"/>).
    /// </summary>
    private static void OnExternalPlay()
    {
        if (_externalHold) return;
        _externalHold = true;
        Signals.Clear();
        _waitingForGame = false;
        foreach (var session in AllSessions()) session.Halt();
        _panel?.ShowTyping(false);
        Log.Info("chat: an outside client acts on the game; chat agents stopped");
        const string note = "외부 에이전트가 게임을 진행하기 시작해서 인게임 에이전트를 멈췄어요. 채팅창에 메시지를 보내면 다시 이어서 해요.";
        if (_active != null) Record(_active, new Item("note", note));
        _panel?.AddNote(note);
    }

    /// <summary>While an outside client has the game, the chat sends nothing on its own.</summary>
    private static bool StepBackForExternal()
    {
        if (!_externalHold) return false;
        Signals.Clear();
        return true;
    }

    /// <summary>
    /// Main thread. Why the chat's agent may not act now (null: it may): an outside client is playing; the
    /// commentary level; or, at co-play (Low), a turn the chat started itself (the player did not ask).
    /// </summary>
    public static string? ActRefusal()
    {
        if (_externalHold || Tools.ExternalPlay.Running)
            return "Another MCP client is playing this game right now; do not act. Tell the player in one line if needed.";
        if (_active?.Role != ChatRole.Play) return null;
        if (Level == PlayLevel.Commentary)
            return "Play level is commentary: you do not act. Comment only; the player plays.";
        if (Level == PlayLevel.CoLow && _autoTurn)
            return "Play level is co-play (Low) and this turn is an automatic notice, not a player's request: do not act. " +
                   "Say what matters for the player's next decision in a line or two; the player plays.";
        return null;
    }

    /// <summary>Which moments get an automatic message, per play level. In the levels where the agent plays, the
    /// continue loop drives it instead.</summary>
    private static bool Wants(PlayLevel level, string kind) => level switch
    {
        PlayLevel.Commentary => kind is "combat_end" or "room" or "deck",
        PlayLevel.CoLow => kind is "combat_start" or "turn" or "combat_end" or "room" or "deck",
        _ => false,
    };

    private static bool Connected => _auth is { Ready: true } && _panel?.Connected == true;

    /// <summary>Every half second: sends the moments collected so far once the session is free and the player is
    /// not typing.</summary>
    private static void TryAuto()
    {
        if (Signals.Count == 0) return;
        var session = _active;
        if (session?.Role != ChatRole.Play || !Connected)
        {
            Signals.Clear();
            return;
        }
        // Not while a question waits for the player.
        if (!session.Idle || PlayerTyping() || _pendingQuestion != null || _waitingForGame || DateTime.UtcNow - _lastAuto < AutoGap) return;
        string what = string.Join(", ", Signals.Distinct());
        Signals.Clear();
        SendAuto(session, what);
    }

    private static bool PlayerTyping() =>
        DateTime.UtcNow - _lastUserMessage < TypingGrace || (_panel?.Input.Text.Length ?? 0) > 0;

    private static void SendAuto(AgentSession session, string what, Action<string>? onReply = null)
    {
        if (StepBackForExternal()) return;
        _autoTurn = true;
        _lastAuto = DateTime.UtcNow;
        string text = "[자동] " + what;
        Record(session, new Item("note", "자동: " + what));
        if (session == _active) _panel?.AddNote("자동: " + what);
        session.Send(Compose(session, text), onReply);
    }

    private static void RequestReport()
    {
        var session = _play;
        string? key = session?.RunKey;
        if (session == null || key == null || !session.Started || !ReportRequested.Add(key)) return;
        Signals.Clear();
        Log.Info($"chat: asking the play session for its run report ({key})");
        const string note = "런이 끝나 보고를 요청했어요.";
        Record(session, new Item("note", note));
        if (session == _active) _panel?.AddNote(note);
        session.Send(Compose(session, "[런 종료] 런이 끝났다. state로 결과 화면을 확인하고 관제에게 넘길 보고를 써라."),
            reply => MainThread.Run(() => ReportReceived(session, key, reply)));
    }

    private static void ReportReceived(AgentSession session, string key, string reply)
    {
        _store.MarkEnded(key);
        if (reply.Trim().Length > 0)
        {
            _store.SaveReport(key, reply);
            _store.PendingReport = reply;
        }
        if (_active != session) session.Stop();
        Log.Info($"chat: run report received ({key}, {reply.Length} chars)");
    }

    // ---- turns ----

    private static void OnEvent(AgentSession session, AgentEvent ev)
    {
        bool active = session == _active;
        switch (ev)
        {
            case AgentEvent.Text t:
            {
                // Built now, while the combat ids in it still resolve; kept for drawing the transcript again.
                var markup = Markup.Parse(t.Content);
                Record(session, new Item("agent", t.Content, Markup: markup));
                if (active) _panel?.AddAgent(markup);
                break;
            }
            case AgentEvent.ToolUse u:
            {
                string line = Describe(u.Name, u.Input);
                Record(session, new Item("note", line));
                if (active) _panel?.AddNote(line);
                break;
            }
            case AgentEvent.Note n:
                Record(session, new Item("note", n.Content, n.Error));
                if (active) _panel?.AddNote(n.Content, n.Error);
                break;
            case AgentEvent.Busy b:
                if (active) _panel?.ShowTyping(b.On);
                break;
            case AgentEvent.Status s:
                if (active && _auth is { Ready: true }) _panel?.SetStatus(StatusPrefix() + s.Message);
                break;
            case AgentEvent.AuthLost lost:
                foreach (var each in AllSessions()) each.Stop();
                AuthLost?.Invoke(lost.Reason);
                break;
            case AgentEvent.TurnEnded ended:
                AfterTurn(session, ended);
                break;
        }
    }

    /// <summary>In the levels where the agent plays, it goes on by itself until it asks something (AskUserQuestion),
    /// the player stops it, or the run ends.</summary>
    private static void AfterTurn(AgentSession session, AgentEvent.TurnEnded ended)
    {
        if (session != _play || session != _active || ended.Error) return;
        if (Level is not (PlayLevel.CoHigh or PlayLevel.Auto) || _autoPaused || !Connected) return;
        if (_pendingQuestion != null || _waitingForGame || GameWatch.RunKey != session.RunKey || ReportRequested.Contains(session.RunKey ?? "")) return;
        if (++_continues > MaxContinues)
        {
            _panel?.AddNote("자동 진행을 잠시 멈췄어요. 계속하려면 메시지를 보내 주세요.");
            return;
        }
        Task.Delay(1500).ContinueWith(_ => MainThread.Run(() =>
        {
            if (session != _active || !session.Idle || PlayerTyping() || _autoPaused) return;
            SendAuto(session, "계속");
        }));
    }

    /// <summary>What actually goes to the session: the brief (first message of a run), the last run's report (control),
    /// a play level change, and the game log since the last message, then the text.</summary>
    private static string Compose(AgentSession session, string text)
    {
        var sb = new StringBuilder();
        if (session.Role == ChatRole.Play)
        {
            if (Briefed.Add(session))
            {
                _levelChanged = false;
                sb.Append(RunBrief()).Append("\n\n");
            }
            else if (_levelChanged && session.Idle)
            {
                _levelChanged = false;
                // The level is in the system prompt: start the process again so it is read (the conversation resumes).
                session.Stop();
                sb.Append($"[설정] 플레이 단계가 {Prompts.LevelName(Level)}(으)로 바뀌었다.\n\n");
            }
            if (GameLog() is { } log) sb.Append(log).Append("\n\n");
        }
        else if (_store.PendingReport is { } report)
        {
            _store.PendingReport = null;
            sb.Append("[지난 런 보고]\n").Append(OneBlock(report)).Append("\n\n");
        }
        sb.Append(text);
        return sb.ToString();
    }

    /// <summary>Text without blank lines: the blocks put before a message end at the first blank line (so a saved
    /// conversation can be shown again without them, see <see cref="PlayerText"/>).</summary>
    private static string OneBlock(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0);
        return string.Join("\n", lines);
    }

    // ---- a resumed conversation, shown again ----

    private static readonly string[] AddedBlocks = { "[런 브리핑]", "[게임 기록]", "[지난 런 보고]", "[설정]" };

    /// <summary>
    /// The items of a saved conversation (Claude Code's <c>projects/.../&lt;id&gt;.jsonl</c> in the game's config
    /// folder): the player's messages without the blocks the chat put before them, automatic messages as notes,
    /// the agent's text, tool calls as notes, and question cards with the answers given. Main thread.
    /// </summary>
    private static List<Item> LoadHistory(string sessionId)
    {
        var items = new List<Item>();
        try
        {
            string dir = Path.Combine(ClaudeCli.ConfigDir, "projects");
            string? file = Directory.Exists(dir)
                ? Directory.GetFiles(dir, sessionId + ".jsonl", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (file == null) return items;
            Item? question = null;
            foreach (var line in File.ReadLines(file))
            {
                JsonObject? ev;
                try { ev = JsonNode.Parse(line) as JsonObject; }
                catch { continue; }
                if (ev == null || ev["isSidechain"] is JsonValue sc && sc.TryGetValue(out bool side) && side) continue;
                string type = ev["type"]?.ToString() ?? "";
                var content = ev["message"]?["content"];
                if (type == "user")
                {
                    string text = content is JsonValue v ? v.ToString()
                        : string.Join("\n", (content as JsonArray)?.OfType<JsonObject>()
                            .Where(b => b["type"]?.ToString() == "text")
                            .Select(b => b["text"]?.ToString() ?? "") ?? Enumerable.Empty<string>());
                    if (text.Trim().Length == 0) continue; // tool results
                    var (kind, shown) = PlayerText(text);
                    if (kind == null) continue;
                    if (kind == "user" && question != null)
                    {
                        Answers[question] = ParseAnswers(question.Questions!, shown);
                        question = null;
                        continue;
                    }
                    question = null;
                    items.Add(new Item(kind, shown));
                }
                else if (type == "assistant")
                {
                    foreach (var block in (content as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    {
                        switch (block["type"]?.ToString())
                        {
                            case "text" when (block["text"]?.ToString() ?? "").Trim() is { Length: > 0 } said:
                                items.Add(new Item("agent", said, Markup: Markup.Parse(said)));
                                break;
                            case "tool_use":
                            {
                                string name = block["name"]?.ToString() ?? "";
                                var input = block["input"] as JsonObject;
                                if (name.EndsWith("AskUserQuestion") && input != null)
                                {
                                    try
                                    {
                                        question = new Item("question", "", Questions: Tools.AskUserQuestionTool.Parse(input));
                                        items.Add(question);
                                    }
                                    catch { /* malformed call: nothing to show */ }
                                }
                                else items.Add(new Item("note", Describe(name, input)));
                                break;
                            }
                        }
                    }
                }
            }
            if (items.Count > 0) items.Insert(0, new Item("note", "이전 대화를 이어서 불러왔어요."));
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: conversation {sessionId} not shown again: {ex.Message}");
        }
        return items;
    }

    /// <summary>A saved player message as shown: (kind, text), kind null for nothing to show.</summary>
    private static (string? Kind, string Text) PlayerText(string text)
    {
        text = text.Replace("\r", "").Trim();
        while (AddedBlocks.Any(b => text.StartsWith(b, StringComparison.Ordinal)))
        {
            int end = text.IndexOf("\n\n", StringComparison.Ordinal);
            text = end < 0 ? "" : text[(end + 2)..].TrimStart();
        }
        if (text.Length == 0) return (null, "");
        if (text.StartsWith("[자동] ", StringComparison.Ordinal)) return ("note", "자동: " + text["[자동] ".Length..]);
        if (text.StartsWith("[런 종료]", StringComparison.Ordinal)) return ("note", "런이 끝나 보고를 요청했어요.");
        return ("user", text);
    }

    /// <summary>The answer message of a question card ("header: choice" per line) back into one answer per question.</summary>
    private static IReadOnlyList<string> ParseAnswers(IReadOnlyList<AskQuestion> questions, string text)
    {
        var lines = text.Split('\n');
        return questions.Select(q =>
        {
            var line = lines.FirstOrDefault(l => l.StartsWith(q.Header + ": ", StringComparison.Ordinal));
            return line != null ? line[(q.Header.Length + 2)..].Trim() : "(답 없음)";
        }).ToList();
    }

    private static string RunBrief()
    {
        var sb = new StringBuilder("[런 브리핑]\n");
        sb.Append("플레이 단계: ").Append(Prompts.LevelName(Level)).Append('\n');
        if (_store.Brief is { } brief)
        {
            sb.Append("관제 브리핑: ").Append(OneBlock(brief)).Append('\n');
            _store.Brief = null;
        }
        string notes = Prompts.Notes();
        if (notes.Length > 0) sb.Append("학습 메모:\n").Append(OneBlock(notes)).Append('\n');
        return sb.ToString().TrimEnd();
    }

    /// <summary>The game since the last message: event log lines and the views the player opened.</summary>
    private static string? GameLog()
    {
        var lines = EventLog.Since(_logMark);
        _logMark = EventLog.Mark();
        if (lines.Count > MaxLogLines) lines = new[] { $"(앞의 {lines.Count - MaxLogLines}줄 생략)" }.Concat(lines.Skip(lines.Count - MaxLogLines)).ToList();
        var views = Views.Distinct().ToList();
        Views.Clear();
        if (lines.Count == 0 && views.Count == 0) return null;
        var sb = new StringBuilder("[게임 기록]");
        foreach (var line in lines) sb.Append("\n- ").Append(line);
        if (views.Count > 0) sb.Append("\n- 플레이어가 열어 본 화면: ").Append(string.Join(", ", views));
        return sb.ToString();
    }

    private static bool IsStopWord(string text) =>
        text.Trim() is "멈춰" or "멈춰줘" or "정지" or "그만" or "stop" or "잠깐" or "기다려";

    private static bool IsGoWord(string text) =>
        text.Trim() is "계속" or "계속해" or "진행" or "진행해" or "go" or "ㄱㄱ";

    /// <summary>One short line per tool call (the game font has no emoji, so plain marks only).</summary>
    private static string Describe(string name, JsonObject? input)
    {
        string tool = name.StartsWith("mcp__sts2__") ? name["mcp__sts2__".Length..] : name;
        string List(string key) => input?[key] is JsonArray a ? string.Join(", ", a.Select(x => x?.ToString())) : "";
        string text = tool switch
        {
            "state" => "상황 확인",
            "act" => "행동: " + List("actions"),
            "info" => "정보 조회: " + List("ids"),
            "search" => "검색: " + string.Join(" ", new[] { "query", "kind", "pool", "type", "rarity" }
                .Select(k => input?[k]?.ToString()).Where(v => !string.IsNullOrEmpty(v))),
            "show_cards" => "카드 보여 주기",
            "show_relics" => "유물 보여 주기",
            "open_view" => "화면 열기: " + input?["view"],
            "map_route" => "지도에 경로 표시",
            "notes" => "학습 메모 " + (input?["text"] != null ? "정리" : "확인"),
            "play_level" => "플레이 단계 " + (input?["level"] != null ? "변경" : "확인"),
            "run_brief" => "런 브리핑 작성",
            "AskUserQuestion" => "질문",
            "wait_for_player" => "게임에서 고르시길 기다려요",
            _ => tool,
        };
        if (text.Length > 120) text = text[..120] + "…";
        return "· " + text + " ·";
    }
}
