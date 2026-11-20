using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Connects the chat panel to the chat's sessions (<see cref="ChatSessions"/>: player messages out, agent events in)
/// and runs the login (docs/in-game-chat.md §3.1): checks how the chat can run when the panel appears, shows the login
/// card when it cannot, runs Claude Code's own login or installer and watches for the result.
/// </summary>
internal static class ChatController
{
    private static readonly TimeSpan WatchLimit = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(3);

    private static bool _attached;
    private static ChatPanel? _panel;
    private static Config? _config;
    private static AuthState _auth = AuthState.CheckingNow;
    private static int _checkRun;
    private static CancellationTokenSource? _watch;
    /// <summary>
    /// What is under way (shown on the card until the check passes): null, "login" (Claude Code's login without a
    /// window: the browser), "login-window" (the same login in a console window), "install" (installer window).
    /// </summary>
    private static string? _settingUp;
    private static LoginProcess? _login;
    private static bool _loginConsole;

    public static AuthState Auth => _auth;
    public static string? SettingUp => _settingUp;

    public static void Attach(ChatPanel panel, Config config)
    {
        if (_panel == panel) return;
        _panel = panel;
        _config = config;
        bool first = !_attached;
        _attached = true;
        ChatSessions.Attach(panel, config);
        if (first) ChatSessions.AuthLost += OnAuthLost;
        panel.Submitted += ChatSessions.UserMessage;
        panel.LoginPressed += OpenLogin;
        panel.BrowserPressed += OpenBrowser;
        panel.LoginWindowPressed += OpenLoginWindow;
        panel.CancelSetupPressed += CancelSetup;
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
                // While a console window is watched or the browser login runs, the card keeps saying what it waits for.
                if (_watch == null && _login == null) _settingUp = null;
                SetAuth(state);
            });
        });
    }

    private static void SetAuth(AuthState state)
    {
        _auth = state;
        if (state.Ready) _settingUp = null;
        ChatSessions.SetAuth(state);
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

    // ---- Claude Code's own login (browser, or a console window) and installer (console window) ----

    /// <summary>
    /// Claude Code's login without a window (<see cref="LoginProcess"/>): it opens the browser and ends by itself when
    /// the sign-in is done; then the check runs at once.
    /// </summary>
    /// <param name="console">An Anthropic Console login (billed per API use) instead of a Claude subscription.</param>
    private static void OpenLogin(bool console)
    {
        if (_auth.Exe is not { } exe) return;
        StopLogin();
        _loginConsole = console;
        _settingUp = "login";
        _panel?.ShowAuth(_auth, _settingUp);
        Task.Run(async () =>
        {
            var args = await LoginArgs(exe, console);
            await MainThread.Run(() =>
            {
                if (_settingUp != "login") return; // cancelled meanwhile
                if (args == null)
                {
                    _settingUp = null;
                    _panel?.ShowAuth(_auth, _settingUp, "이 버전의 Claude Code는 API 로그인을 지원하지 않아요. 터미널에서 claude update 를 실행해 주세요.");
                    return;
                }
                Log.Info($"chat: starting Claude Code login ({(console ? "console" : "subscription")})");
                LoginProcess? login = null;
                // Started on the main thread, so its callbacks (queued to the main thread) see it assigned.
                login = LoginProcess.Start(exe, args,
                    onUrl: _ => MainThread.Run(() => { if (_login == login) _panel?.ShowAuth(_auth, _settingUp); }),
                    onExit: (code, error) => MainThread.Run(() => LoginEnded(login!, code, error)));
                _login = login;
                if (login == null)
                {
                    _settingUp = null;
                    _panel?.ShowAuth(_auth, _settingUp, "로그인을 시작하지 못했어요. 로그를 확인해 주세요.");
                    return;
                }
                // No polling: the process ends when the sign-in is done. It is given up after the usual wait.
                Task.Delay(WatchLimit).ContinueWith(_ => MainThread.Run(() => { if (_login == login) CancelSetup(); }));
            });
        });
    }

    /// <summary>The login's arguments; null when this version cannot do the asked kind (no --console).</summary>
    private static async Task<string[]?> LoginArgs(string exe, bool console)
    {
        string flag = console ? "--console" : "--claudeai";
        if (await AuthCheck.HasLoginFlag(exe, flag)) return new[] { "auth", "login", flag };
        // Without the flag the login would be a subscription one.
        return console ? null : new[] { "auth", "login" };
    }

    private static void LoginEnded(LoginProcess login, int code, string? error)
    {
        if (_login != login) return;
        _login = null;
        StopWatching();
        if (code == 0)
        {
            Log.Info("chat: login finished");
            Recheck();
            return;
        }
        _settingUp = null;
        _panel?.ShowAuth(_auth, _settingUp, "로그인하지 못했어요" + (error != null ? ": " + error : "."));
    }

    /// <summary>Opens the sign-in address again (the browser did not open, or was closed).</summary>
    private static void OpenBrowser()
    {
        if (_login?.Url is not { } url) return;
        Log.Info("chat: opening the sign-in page again");
        Godot.OS.ShellOpen(url);
    }

    /// <summary>The same login in a console window, for a sign-in that asks for a code to paste.</summary>
    private static void OpenLoginWindow()
    {
        if (_auth.Exe is not { } exe) return;
        StopLogin();
        bool console = _loginConsole;
        _settingUp = "login-window";
        _panel?.ShowAuth(_auth, _settingUp);
        Task.Run(async () =>
        {
            var args = await LoginArgs(exe, console) ?? new[] { "auth", "login" };
            Log.Info($"chat: opening Claude Code login window ({(console ? "console" : "subscription")})");
            bool opened = ClaudeCli.OpenConsole("claude-login", console ? "STS2 Agent - API login" : "STS2 Agent - Claude login", (exe, args));
            await MainThread.Run(() => AfterOpen(opened));
        });
    }

    /// <summary>Stops waiting for a login or install (and ends a windowless login).</summary>
    private static void CancelSetup()
    {
        Log.Info($"chat: {_settingUp ?? "nothing"} cancelled by the player");
        StopLogin();
        StopWatching();
        _settingUp = null;
        _panel?.ShowAuth(_auth, _settingUp);
    }

    private static void StopLogin()
    {
        var login = _login;
        _login = null;
        login?.Cancel();
    }

    /// <summary>Installs only; the card then offers both logins.</summary>
    private static void OpenInstall()
    {
        _settingUp = "install";
        _panel?.ShowAuth(_auth, _settingUp);
        Log.Info("chat: opening Claude Code install");
        bool opened = ClaudeCli.OpenConsole("claude-install", "STS2 Agent - Claude install",
            ("powershell.exe", new[] { "-NoProfile", "-Command", "irm https://claude.ai/install.ps1 | iex" }));
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
    /// Waits for the console window's work: Claude Code running (install), or a saved login in the game's folder (asked
    /// with <c>auth status</c>; the credentials file is never read). Then a full check. Stops after <see cref="WatchLimit"/>.
    /// </summary>
    private static void Watch()
    {
        StopWatching();
        var cts = new CancellationTokenSource(WatchLimit);
        _watch = cts;
        var config = _config!;
        bool installing = _settingUp == "install";
        Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(WatchInterval, cts.Token);
                    string? exe = ClaudeCli.Find(config.ClaudePath);
                    if (exe == null) continue;
                    if (installing ? !await AuthCheck.Runs(exe) : !await AuthCheck.LoggedIn(exe)) continue;
                    Log.Info(installing ? "chat: install found" : "chat: login found");
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
                    StopLogin();
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
        if (_auth.Exe is not { } exe) return;
        StopLogin();
        ChatSessions.StopAll(forget: true);
        _panel?.ShowTyping(false);
        _panel?.SetConnected(false);
        _panel?.AddNote("로그아웃했어요.");
        SetAuth(AuthState.CheckingNow);
        Task.Run(async () =>
        {
            var result = await ClaudeCli.Run(exe, new[] { "auth", "logout" }, TimeSpan.FromSeconds(20));
            Log.Info($"chat: logout ({result.ExitCode}) {result.Stdout} {result.Stderr}".Trim());
            await MainThread.Run(Recheck);
        });
    }

    // ---- agent events ----

    private static void OnAuthLost(string reason)
    {
        Log.Warn($"chat: login lost: {reason}");
        _panel?.ShowTyping(false);
        _panel?.SetConnected(false);
        _panel?.AddNote("로그인이 만료됐어요. 다시 로그인한 뒤 메시지를 보내 주세요.", true);
        Recheck();
    }
}
