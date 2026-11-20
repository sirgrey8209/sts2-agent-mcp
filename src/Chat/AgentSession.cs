using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>What the chat panel shows for agent activity.</summary>
internal abstract record AgentEvent
{
    public sealed record Status(string Message) : AgentEvent;
    public sealed record Text(string Content) : AgentEvent;
    public sealed record ToolUse(string Name, JsonObject? Input) : AgentEvent;
    public sealed record Note(string Content, bool Error) : AgentEvent;
    public sealed record Busy(bool On) : AgentEvent;
    /// <summary>The login stopped working mid-conversation.</summary>
    public sealed record AuthLost(string Reason) : AgentEvent;
    /// <summary>A turn ended; <paramref name="Reply"/> is all its text.</summary>
    public sealed record TurnEnded(string Reply, bool Error) : AgentEvent;
}

/// <summary>
/// Runs Claude Code as a child process for one chat conversation (docs/in-game-chat.md §3): the control session of the
/// main menu, or the play session of one run (docs/chat-roadmap.md). <c>claude -p --input-format stream-json
/// --output-format stream-json</c>, connected only to this mod's chat MCP endpoint (<c>/mcp/chat</c>: the play tools
/// plus the in-game ones), built-in tools off. One process holds one conversation; input is queued and written one turn
/// at a time; the conversation is resumed by id after the process ends. It runs only once <see cref="Auth"/> is ready
/// (subscription or Console login, §3.1), in the game's own Claude Code folder, cut off from the player's own Claude
/// Code setup (CLAUDE.md, skills, settings, env). Events are raised on a background thread.
/// </summary>
internal sealed class AgentSession
{
    public static readonly string[] AllowedTools =
    {
        "mcp__sts2__state", "mcp__sts2__act", "mcp__sts2__info", "mcp__sts2__search",
        "mcp__sts2__show_cards", "mcp__sts2__show_relics", "mcp__sts2__open_view", "mcp__sts2__map_route",
        "mcp__sts2__notes", "mcp__sts2__play_level", "mcp__sts2__run_brief", "mcp__sts2__AskUserQuestion",
        "mcp__sts2__wait_for_player",
    };

    // Fixed for now (docs/chat-roadmap.md): Sonnet at medium effort.
    private const string Model = "sonnet";
    private const string Effort = "medium";

    private sealed record Turn(string Text, Action<string>? OnReply);

    private readonly Config _config;
    private readonly Func<string> _systemPrompt;
    private readonly ConcurrentQueue<Turn> _pending = new();
    private readonly object _gate = new();
    private readonly StringBuilder _reply = new();
    private Turn? _current;
    private Process? _process;
    private bool _busy;
    private string? _sessionId;
    private string? _connection;
    private volatile AuthState? _auth;

    /// <param name="sessionId">A conversation to resume (from an earlier game start), or null.</param>
    /// <param name="systemPrompt">Read each time the process starts, so prompt changes apply on the next start.</param>
    public AgentSession(Config config, ChatRole role, string? runKey, string? sessionId, Func<string> systemPrompt)
    {
        _config = config;
        Role = role;
        RunKey = runKey;
        _sessionId = sessionId;
        _systemPrompt = systemPrompt;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    public ChatRole Role { get; }
    /// <summary>The run this play session belongs to (null for the control session).</summary>
    public string? RunKey { get; }
    public string? SessionId => _sessionId;
    /// <summary>Whether anything was ever sent in this conversation (here or before a restart).</summary>
    public bool Started => _sessionId != null || _sentAny;
    private bool _sentAny;

    public bool Idle
    {
        get { lock (_gate) return !_busy && _pending.IsEmpty; }
    }

    /// <summary>Raised on a background thread.</summary>
    public event Action<AgentEvent>? Event;
    /// <summary>Raised when Claude Code reports the conversation id (to keep it for --resume).</summary>
    public event Action<string>? SessionIdChanged;

    /// <summary>How to run Claude Code. Messages wait in the queue until it is ready.</summary>
    public AuthState? Auth
    {
        get => _auth;
        set
        {
            _auth = value;
            if (value is { Ready: true }) Task.Run(Pump);
        }
    }

    /// <summary>Any thread. Queues a message; starts Claude Code if needed. <paramref name="onReply"/> gets the
    /// whole text of the turn's answer (background thread).</summary>
    public void Send(string text, Action<string>? onReply = null)
    {
        _sentAny = true;
        _pending.Enqueue(new Turn(text, onReply));
        Task.Run(Pump);
    }

    /// <summary>Ends the running process and drops the queued messages; the conversation can be resumed.</summary>
    public void Halt()
    {
        _pending.Clear();
        Stop();
    }

    /// <summary>Ends the running process (the conversation can be resumed). <paramref name="forget"/> also drops
    /// queued messages and the conversation (after a logout the next login may be another account).</summary>
    public void Stop(bool forget = false)
    {
        lock (_gate)
        {
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
            _process = null;
            _busy = false;
            _current = null;
            _connection = null;
            if (forget)
            {
                _pending.Clear();
                _sessionId = null;
            }
        }
    }

    private void Pump()
    {
        Turn? next;
        Process? process;
        lock (_gate)
        {
            if (_busy || _pending.IsEmpty) return;
            if (_auth is not { Ready: true }) return; // waits for the login check
            if (_process == null || _process.HasExited)
            {
                if (!StartProcess(_auth)) { _pending.Clear(); return; }
            }
            if (!_pending.TryDequeue(out next)) return;
            _busy = true;
            _current = next;
            _reply.Clear();
            process = _process!;
        }
        Raise(new AgentEvent.Busy(true));
        Raise(new AgentEvent.Status(_connection != null ? "생각 중… · " + _connection : "생각 중…"));
        try
        {
            var line = new JsonObject
            {
                ["type"] = "user",
                ["message"] = new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = next.Text }),
                },
            };
            process.StandardInput.WriteLine(line.ToJsonString());
            process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            Log.Error($"chat: write to claude failed: {ex.Message}");
            lock (_gate) { _busy = false; _current = null; }
            Raise(new AgentEvent.Busy(false));
            Raise(new AgentEvent.Note("메시지를 보내지 못했어요: " + ex.Message, true));
        }
    }

    private bool StartProcess(AuthState auth)
    {
        string exe = auth.Exe!;
        string dir = WorkDir();
        Directory.CreateDirectory(dir);
        string mcpConfig = Path.Combine(dir, "mcp.json");
        File.WriteAllText(mcpConfig, new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["sts2"] = new JsonObject
                {
                    ["type"] = "http",
                    // The chat endpoint: the play tools plus the in-game ones (external clients use /mcp).
                    ["url"] = $"http://127.0.0.1:{_config.Port}{HttpHost.ChatPath}",
                    ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {_config.Token}" },
                },
            },
        }.ToJsonString());

        // The prompt goes through a file: an npm install starts through cmd.exe, which cannot pass line breaks.
        // One file per role, so a control and a play process never read each other's prompt.
        string promptFile = Path.Combine(dir, $"system-prompt-{(Role == ChatRole.Control ? "control" : "play")}.txt");
        File.WriteAllText(promptFile, _systemPrompt(), new UTF8Encoding(false));

        var args = new List<string>
        {
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--mcp-config", mcpConfig, "--strict-mcp-config",
            "--tools", "",
            "--allowedTools", string.Join(",", AllowedTools),
            "--permission-mode", "dontAsk",
            // The system prompt is given on every start (it is not kept in the conversation), so a resumed
            // conversation runs on the current prompt.
            "--system-prompt-file", promptFile,
            "--model", Model, "--effort", Effort,
            // Nothing of the player's own setup: settings only from the game's config folder (no project/local
            // settings), no skills or slash commands. CLAUDE.md discovery and auto-memory are off (ClaudeCli env).
            "--setting-sources", "user",
            "--disable-slash-commands",
        };
        if (_sessionId != null) args.AddRange(new[] { "--resume", _sessionId });

        var psi = ClaudeCli.StartInfo(exe, args);
        // Wait for the game's MCP server before the first turn. Claude Code 2.1.159 connects MCP servers in the
        // background with stream-json input, so the first turn started with the server "pending" and no game tools.
        psi.Environment["MCP_CONNECTION_NONBLOCKING"] = "false";
        psi.WorkingDirectory = dir;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = new UTF8Encoding(false);
        psi.StandardErrorEncoding = new UTF8Encoding(false);
        psi.StandardInputEncoding = new UTF8Encoding(false);

        try
        {
            var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            ClaudeCli.Adopt(process);
            _process = process;
            _ = Task.Run(() => ReadStdout(process));
            _ = Task.Run(() => ReadStderr(process));
            Log.Info($"chat: started claude {Role}{(RunKey != null ? " " + RunKey : "")} (pid {process.Id}, {auth.Method})" +
                     (_sessionId != null ? " resuming " + _sessionId : ""));
            Raise(new AgentEvent.Status("연결 중…"));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"chat: could not start claude: {ex}");
            Raise(new AgentEvent.Note("Claude Code를 실행하지 못했어요: " + ex.Message, true));
            Raise(new AgentEvent.Status("연결 안 됨"));
            return false;
        }
    }

    private async Task ReadStdout(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (line.Length == 0) continue;
                JsonObject? ev;
                try { ev = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { Log.Warn($"chat: non-JSON line from claude: {Trim(line)}"); continue; }
                if (ev != null) Handle(ev, process);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"chat: reading claude output failed: {ex.Message}");
        }

        int code = -1;
        try { process.WaitForExit(2000); code = process.ExitCode; } catch { /* ignore */ }
        bool wasBusy;
        lock (_gate)
        {
            if (_process != process) return; // replaced or stopped on purpose
            _process = null;
            wasBusy = _busy;
            _busy = false;
            _current = null;
        }
        Log.Warn($"chat: claude exited ({code})");
        Raise(new AgentEvent.Busy(false));
        Raise(new AgentEvent.Status("연결 끊김 · 다음 메시지에 다시 연결해요"));
        if (wasBusy)
        {
            Raise(new AgentEvent.Note($"Claude Code가 종료됐어요 (코드 {code}).", true));
            Raise(new AgentEvent.TurnEnded("", true));
        }
    }

    private static async Task ReadStderr(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
                if (line.Length > 0) Log.Warn($"chat: claude stderr: {Trim(line)}");
        }
        catch { /* process gone */ }
    }

    private void Handle(JsonObject ev, Process process)
    {
        lock (_gate) if (_process != process) return; // a stopped process's last words
        string type = Str(ev["type"]);
        string subtype = Str(ev["subtype"]);
        switch (type)
        {
            case "system" when subtype == "init":
            {
                if (Str(ev["session_id"]) is { Length: > 0 } id && id != _sessionId)
                {
                    _sessionId = id;
                    try { SessionIdChanged?.Invoke(id); } catch (Exception ex) { Log.Error($"chat: session id handler failed: {ex}"); }
                }
                string model = Str(ev["model"]);
                var servers = (ev["mcp_servers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
                var sts2 = servers.FirstOrDefault(s => Str(s["name"]) == "sts2");
                string mcp = sts2 == null ? "게임 도구 없음" : Str(sts2["status"]) == "connected" ? "게임 연결됨" : "게임 도구 " + Str(sts2["status"]);
                string auth = _auth?.Label ?? "";
                _connection = auth.Length > 0 ? $"{auth} · {mcp} · {model}" : $"{mcp} · {model}";
                Raise(new AgentEvent.Status("생각 중… · " + _connection));
                if (sts2 == null || Str(sts2["status"]) != "connected")
                    Raise(new AgentEvent.Note("게임 도구(MCP)에 연결되지 않았어요. 게임 상태를 볼 수 없어요.", true));
                break;
            }
            case "system" when subtype == "compact_boundary":
                Raise(new AgentEvent.Note("대화가 길어져 앞부분을 요약했어요.", false));
                break;
            case "assistant":
                foreach (var block in (ev["message"]?["content"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    switch (Str(block["type"]))
                    {
                        case "text" when Str(block["text"]).Trim() is { Length: > 0 } text:
                            lock (_gate)
                            {
                                if (_reply.Length > 0) _reply.Append("\n\n");
                                _reply.Append(text);
                            }
                            Raise(new AgentEvent.Text(text));
                            break;
                        case "tool_use":
                            Raise(new AgentEvent.ToolUse(Str(block["name"]), block["input"] as JsonObject));
                            break;
                    }
                }
                break;
            case "result":
            {
                bool isError = ev["is_error"] is JsonValue v && v.TryGetValue(out bool e) && e;
                string reason = Str(ev["result"]) is { Length: > 0 } r ? r : subtype;
                Turn? turn;
                string reply;
                lock (_gate)
                {
                    _busy = false;
                    turn = _current;
                    _current = null;
                    reply = _reply.ToString();
                }
                bool failed = isError || subtype is not ("success" or "");
                if (failed && ClaudeCli.IsLoginError(reason))
                {
                    // The controller checks the login again and shows the login card; the queue waits for it.
                    Raise(new AgentEvent.Busy(false));
                    Raise(new AgentEvent.AuthLost(reason));
                    break;
                }
                if (failed) Raise(new AgentEvent.Note("오류: " + reason, true));
                Raise(new AgentEvent.Busy(false));
                Raise(new AgentEvent.Status(_connection != null ? "대기 중 · " + _connection : "대기 중"));
                if (turn?.OnReply != null)
                {
                    try { turn.OnReply(reply); }
                    catch (Exception ex) { Log.Error($"chat: reply handler failed: {ex}"); }
                }
                Raise(new AgentEvent.TurnEnded(reply, failed));
                Task.Run(Pump);
                break;
            }
        }
    }

    private void Raise(AgentEvent ev)
    {
        try { Event?.Invoke(ev); }
        catch (Exception ex) { Log.Error($"chat: event handler failed: {ex}"); }
    }

    /// <summary>Claude Code keys conversations by working directory, so this must stay put for --resume.</summary>
    private static string WorkDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "chat");

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
