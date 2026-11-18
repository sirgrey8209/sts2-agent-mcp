using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Connects the chat panel to the Claude Code session: player messages out, agent events in (main thread), and the
/// login (docs/in-game-chat.md §3.1): checks how the chat can run when the panel appears, shows the login card when
/// it cannot, opens Claude Code's own login or installer in a console window and watches for the result.
/// </summary>
internal static class ChatController
{
    private static readonly TimeSpan WatchLimit = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(3);

    private static AgentSession? _session;
    private static ChatPanel? _panel;
    private static Config? _config;
    private static AuthState _auth = AuthState.CheckingNow;
    private static int _checkRun;
    private static CancellationTokenSource? _watch;
    /// <summary>What the open console window is doing (shown on the card until the check passes): null, "login", "install".</summary>
    private static string? _settingUp;

    public static AuthState Auth => _auth;
    public static string? SettingUp => _settingUp;

    public static void Attach(ChatPanel panel, Config config)
    {
        if (_panel == panel) return;
        _panel = panel;
        _config = config;
        bool first = _session == null;
        _session ??= new AgentSession(config);
        if (first) _session.Event += ev => MainThread.Run(() => Show(ev));
        panel.Submitted += text =>
        {
            panel.AddUser(text);
            _session.Send(text);
        };
        panel.LoginPressed += OpenLogin;
        panel.InstallPressed += OpenInstall;
        panel.RecheckPressed += () => Recheck();
        panel.LogoutPressed += Logout;
        panel.AgentChosen += agent => _config?.SaveChatAgent(agent);
        panel.OpenInitial(config.ChatAgent);
        panel.ConnectPressed += remember =>
        {
            if (remember) _config?.SaveChatAutoConnect(true);
            Log.Info($"chat: connected by the player (auto next time: {remember})");
            UpdateStatus();
        };
        panel.ShowAuth(_auth, _settingUp);
        if (first) Recheck();
    }

    /// <summary>Main thread. Checks again (startup, card button, after a login, after a logout).</summary>
    public static void Recheck()
    {
        if (_config == null) return;
        int run = ++_checkRun;
        SetAuth(AuthState.CheckingNow);
        var config = _config;
        Task.Run(async () =>
        {
            var state = await AuthCheck.Run(config);
            Log.Info($"chat: auth {state}");
            await MainThread.Run(() =>
            {
                if (run != _checkRun) return; // a newer check is running
                if (state.Ready) StopWatching();
                // While a console window is still being watched, the card keeps saying what it waits for.
                if (_watch == null) _settingUp = null;
                SetAuth(state);
            });
        });
    }

    private static void SetAuth(AuthState state)
    {
        _auth = state;
        if (state.Ready) _settingUp = null;
        if (_session != null) _session.Auth = state;
        // "Connect automatically" skips the card's [접속] once the login works.
        if (state.Ready && _config?.ChatAutoConnect == true) _panel?.SetConnected(true);
        _panel?.ShowAuth(state, _settingUp);
        UpdateStatus();
    }

    private static void UpdateStatus()
    {
        if (_panel == null) return;
        if (_auth.Checking) _panel.SetStatus("연결 확인 중…");
        else if (!_auth.Ready) _panel.SetStatus("연결 안 됨");
        else if (!_panel.Connected) _panel.SetStatus($"{_auth.Label} · 접속 대기");
        else _panel.SetStatus($"{_auth.Label} · 메시지를 보내면 연결해요");
    }

    // ---- console windows: Claude Code's own login and installer ----

    private static void OpenLogin()
    {
        if (_auth.Exe is not { } exe) return;
        _settingUp = "login";
        _panel?.ShowAuth(_auth, _settingUp);
        Task.Run(async () =>
        {
            bool flag = await AuthCheck.HasClaudeAiFlag(exe);
            var args = flag ? new[] { "auth", "login", "--claudeai" } : new[] { "auth", "login" };
            Log.Info("chat: opening Claude Code login");
            bool opened = ClaudeCli.OpenConsole("claude-login", "STS2 Agent - Claude login", (exe, args));
            await MainThread.Run(() => AfterOpen(opened));
        });
    }

    private static void OpenInstall()
    {
        _settingUp = "install";
        _panel?.ShowAuth(_auth, _settingUp);
        Log.Info("chat: opening Claude Code install and login");
        bool opened = ClaudeCli.OpenConsole("claude-install", "STS2 Agent - Claude install",
            ("powershell.exe", new[] { "-NoProfile", "-Command", "irm https://claude.ai/install.ps1 | iex" }),
            (ClaudeCli.NativeInstallPath, new[] { "auth", "login", "--claudeai" }));
        AfterOpen(opened);
    }

    private static void AfterOpen(bool opened)
    {
        if (!opened)
        {
            _settingUp = null;
            _panel?.ShowAuth(_auth, _settingUp, "창을 열지 못했어요. 로그를 확인해 주세요.");
            return;
        }
        Watch();
    }

    /// <summary>
    /// Waits for the console window's work: Claude Code appearing, then a saved login in the game's folder (asked with
    /// <c>auth status</c>; the credentials file is never read). Then a full check. Stops after <see cref="WatchLimit"/>.
    /// </summary>
    private static void Watch()
    {
        StopWatching();
        var cts = new CancellationTokenSource(WatchLimit);
        _watch = cts;
        var config = _config!;
        Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(WatchInterval, cts.Token);
                    string? exe = ClaudeCli.Find(config.ClaudePath);
                    if (exe == null || !await AuthCheck.LoggedIn(exe)) continue;
                    Log.Info("chat: login found");
                    await MainThread.Run(() =>
                    {
                        if (_watch != cts) return;
                        _watch = null;
                        Recheck();
                    });
                    return;
                }
            }
            catch (OperationCanceledException) { /* stopped or timed out */ }
            if (cts.IsCancellationRequested && _watch == cts)
                await MainThread.Run(() =>
                {
                    if (_watch != cts) return;
                    _watch = null;
                    _settingUp = null;
                    _panel?.ShowAuth(_auth, _settingUp);
                });
        });
    }

    private static void StopWatching()
    {
        var watch = _watch;
        _watch = null;
        try { watch?.Cancel(); } catch { /* disposed */ }
    }

    private static void Logout()
    {
        if (_auth.Exe is not { } exe || _session == null) return;
        _session.Stop(forget: true);
        _panel?.ShowTyping(false);
        _panel?.SetConnected(false);
        _panel?.AddNote("로그아웃했어요.");
        SetAuth(AuthState.CheckingNow);
        Task.Run(async () =>
        {
            var result = await ClaudeCli.Run(exe, new[] { "auth", "logout" }, null, TimeSpan.FromSeconds(20));
            Log.Info($"chat: logout ({result.ExitCode}) {result.Stdout} {result.Stderr}".Trim());
            await MainThread.Run(Recheck);
        });
    }

    // ---- agent events ----

    private static void Show(AgentEvent ev)
    {
        var panel = _panel;
        if (panel == null) return;
        switch (ev)
        {
            case AgentEvent.Status s: if (_auth.Ready) panel.SetStatus(s.Message); break;
            case AgentEvent.Text t: panel.AddAgent(t.Content); break;
            case AgentEvent.ToolUse u: panel.AddNote(Describe(u.Name, u.Input)); break;
            case AgentEvent.Note n: panel.AddNote(n.Content, n.Error); break;
            case AgentEvent.Busy b: panel.ShowTyping(b.On); break;
            case AgentEvent.AuthLost lost:
                Log.Warn($"chat: login lost: {lost.Reason}");
                _session?.Stop();
                panel.SetConnected(false);
                panel.AddNote("로그인이 만료됐어요. 다시 로그인한 뒤 메시지를 보내 주세요.", true);
                Recheck();
                break;
        }
    }

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
            _ => tool,
        };
        if (text.Length > 120) text = text[..120] + "…";
        return "· " + text + " ·";
    }
}
