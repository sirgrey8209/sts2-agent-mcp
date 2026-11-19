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
}

/// <summary>
/// Runs Claude Code as a child process for the in-game chat (docs/in-game-chat.md §3):
/// <c>claude -p --input-format stream-json --output-format stream-json</c>, connected only to this mod's MCP endpoint,
/// built-in tools off, only the play tools allowed. One process holds one conversation; chat input is queued and written
/// one turn at a time. It runs only once <see cref="Auth"/> is ready (subscription or the player's API key, §3.1),
/// in the game's own Claude Code folder. Events are raised on a background thread.
/// </summary>
internal sealed class AgentSession
{
    private static readonly string[] AllowedTools =
        { "mcp__sts2__state", "mcp__sts2__act", "mcp__sts2__info", "mcp__sts2__search" };

    private const string SystemPrompt =
        "너는 Slay the Spire 2 게임 화면 왼쪽 채팅창에 있는 플레이 동료다. 플레이어와 한국어로 대화한다.\n" +
        "- 게임은 sts2 MCP 도구로 본다. state: 현재 화면과 할 수 있는 행동, info: 카드·유물·포션·몬스터·이벤트 설명, " +
        "search: 카드·유물·포션 검색, act: 행동 실행.\n" +
        "- 플레이어가 직접 플레이한다. 기본은 조언이다. act는 플레이어가 해 달라고 할 때만 쓰고, 무엇을 할지 애매하면 먼저 묻는다.\n" +
        "- 판단하기 전에 state로 지금 화면을 확인한다. 이전 대화의 상태를 그대로 믿지 않는다(플레이어가 그사이 진행했을 수 있다).\n" +
        "- 채팅창이 좁다. 짧게 답한다(보통 1~4문장). 표, 코드 블록, 제목을 쓰지 않는다. 강조는 **굵게**만, 목록은 '- '만 쓴다.\n" +
        "- 카드·유물·적 이름은 게임에 표시된 이름을 쓴다.";

    private readonly Config _config;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly object _gate = new();
    private Process? _process;
    private bool _busy;
    private string? _sessionId;
    private string? _connection;
    private volatile AuthState? _auth;

    public AgentSession(Config config)
    {
        _config = config;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    /// <summary>Raised on a background thread.</summary>
    public event Action<AgentEvent>? Event;

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

    /// <summary>Any thread. Queues a chat message; starts Claude Code if needed.</summary>
    public void Send(string text)
    {
        _pending.Enqueue(text);
        Task.Run(Pump);
    }

    /// <summary>Ends the running process. <paramref name="forget"/> also drops queued messages and the conversation
    /// (after a logout the next login may be another account).</summary>
    public void Stop(bool forget = false)
    {
        lock (_gate)
        {
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
            _process = null;
            _busy = false;
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
        string? next;
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
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = next }),
                },
            };
            process.StandardInput.WriteLine(line.ToJsonString());
            process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            Log.Error($"chat: write to claude failed: {ex.Message}");
            lock (_gate) _busy = false;
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
                    ["url"] = $"http://127.0.0.1:{_config.Port}/mcp",
                    ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {_config.Token}" },
                },
            },
        }.ToJsonString());

        // The prompt goes through a file: an npm install starts through cmd.exe, which cannot pass line breaks.
        string promptFile = Path.Combine(dir, "system-prompt.txt");
        File.WriteAllText(promptFile, SystemPrompt, new UTF8Encoding(false));

        var args = new List<string>
        {
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--mcp-config", mcpConfig, "--strict-mcp-config",
            "--tools", "",
            "--allowedTools", string.Join(",", AllowedTools),
            "--permission-mode", "dontAsk",
            "--system-prompt-file", promptFile,
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
            Log.Info($"chat: started claude (pid {process.Id}, {auth.Method}){(_sessionId != null ? " resuming " + _sessionId : "")}");
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
                if (ev != null) Handle(ev);
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
        }
        Log.Warn($"chat: claude exited ({code})");
        Raise(new AgentEvent.Busy(false));
        Raise(new AgentEvent.Status("연결 끊김 · 다음 메시지에 다시 연결해요"));
        if (wasBusy) Raise(new AgentEvent.Note($"Claude Code가 종료됐어요 (코드 {code}).", true));
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

    private void Handle(JsonObject ev)
    {
        string type = Str(ev["type"]);
        string subtype = Str(ev["subtype"]);
        switch (type)
        {
            case "system" when subtype == "init":
            {
                _sessionId = Str(ev["session_id"]) is { Length: > 0 } id ? id : _sessionId;
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
                lock (_gate) _busy = false;
                if ((isError || subtype is not ("success" or "")) && ClaudeCli.IsLoginError(reason))
                {
                    // The controller checks the login again and shows the login card; the queue waits for it.
                    Raise(new AgentEvent.Busy(false));
                    Raise(new AgentEvent.AuthLost(reason));
                    break;
                }
                if (isError || subtype is not ("success" or ""))
                    Raise(new AgentEvent.Note("오류: " + reason, true));
                Raise(new AgentEvent.Busy(false));
                Raise(new AgentEvent.Status(_connection != null ? "대기 중 · " + _connection : "대기 중"));
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
