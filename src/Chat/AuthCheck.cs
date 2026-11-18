using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>How the chat runs: a Claude subscription login, or an Anthropic Console login (billed per API use).</summary>
internal enum AuthMethod { None, Subscription, Console }

/// <summary>
/// What the chat knows about the player's Claude Code: installed or not, the account and plan, and how the chat will
/// run (its subscription login or its Console login). <see cref="Problem"/> says what stands in the way;
/// <see cref="CanLogin"/>/<see cref="CanInstall"/> say which button fixes it.
/// </summary>
internal sealed record AuthState
{
    public bool Checking { get; init; }
    public bool Installed { get; init; }
    public string? Exe { get; init; }
    public string? Version { get; init; }
    /// <summary>A login is saved in the game's folder (it may still not work: Free plan, expired, ...).</summary>
    public bool LoggedIn { get; init; }
    public string? Account { get; init; }
    public string? Plan { get; init; }
    public AuthMethod Method { get; init; }
    public string? Problem { get; init; }
    public bool CanLogin { get; init; }
    public bool CanInstall { get; init; }

    public bool Ready => !Checking && Method != AuthMethod.None && Exe != null;

    /// <summary>Short label for the panel header.</summary>
    public string Label => Method switch
    {
        AuthMethod.Subscription => Plan != null ? $"구독({Plan})" : "구독",
        AuthMethod.Console => "API",
        _ => "",
    };

    public static AuthState CheckingNow => new() { Checking = true };

    public override string ToString() =>
        Checking ? "checking" :
        $"{(Ready ? "ready" : "not ready")} method={Method} installed={Installed} exe={Exe ?? "-"} version={Version ?? "-"} " +
        $"account={Account ?? "-"} plan={Plan ?? "-"} login={CanLogin} install={CanInstall} problem={Problem ?? "-"}";
}

/// <summary>
/// Decides how the chat can run (docs/in-game-chat.md §3.1): <c>--version</c>, <c>auth status</c> (without an API key)
/// and one real request at once, because <c>auth status</c> only reads the saved login and calls an expired one
/// logged in. The login is either a Claude subscription (<c>auth login --claudeai</c>) or an Anthropic Console account
/// (<c>auth login --console</c>, saved as a key Claude Code made for it, billed per use). The player's own
/// <c>ANTHROPIC_API_KEY</c> is not used.
/// Credentials are never read here; only Claude Code's own answers are.
/// </summary>
internal static class AuthCheck
{
    private static readonly Dictionary<string, string> PlanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["max"] = "Max", ["pro"] = "Pro", ["plus"] = "Plus", ["free"] = "Free", ["team"] = "Team",
        ["enterprise"] = "Enterprise", ["business"] = "Business", ["edu"] = "Edu",
    };

    private static string ProbeDir =>
        Path.Combine(Path.GetDirectoryName(ClaudeCli.ConfigDir)!, "probe");

    /// <summary>Never throws: a failure is shown as what is missing.</summary>
    public static async Task<AuthState> Run(Config config)
    {
        try { return await RunCore(config); }
        catch (Exception ex)
        {
            Log.Error($"chat: auth check failed: {ex}");
            return new AuthState { Problem = "확인하지 못했어요: " + ex.Message };
        }
    }

    private static async Task<AuthState> RunCore(Config config)
    {
        string? exe = ClaudeCli.Find(config.ClaudePath);
        if (exe == null)
            return new AuthState { CanInstall = true, Problem = "Claude Code가 설치되어 있지 않아요." };

        var version = ClaudeCli.Run(exe, new[] { "--version" }, TimeSpan.FromSeconds(20));
        var status = ClaudeCli.Run(exe, new[] { "auth", "status" }, TimeSpan.FromSeconds(20));
        var tried = TryRequest(exe);
        await Task.WhenAll(version, status, tried);

        if (!version.Result.Ok)
            return new AuthState { Exe = exe, CanInstall = true, Problem = "Claude Code를 실행하지 못했어요." };
        var state = new AuthState
        {
            Installed = true,
            Exe = exe,
            Version = version.Result.Stdout.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
        };

        JsonObject? json = null;
        try { json = JsonNode.Parse(status.Result.Stdout) as JsonObject; }
        catch { /* not JSON */ }
        if (json == null)
            return state with { Problem = "Claude Code가 오래된 버전이에요. 터미널에서 claude update 를 실행해 주세요." };

        bool loggedIn = json["loggedIn"] is JsonValue li && li.TryGetValue(out bool b) && b;
        string? account = Str(json["email"]);
        string? planId = Str(json["subscriptionType"]);
        string? provider = Str(json["apiProvider"]);
        // A Console login (auth login --console) is saved as a key Claude Code made for it.
        bool console = Str(json["apiKeySource"]) == "/login managed key";
        string? plan = console ? "API (사용량 결제)"
            : planId == null ? null : PlanNames.TryGetValue(planId, out var name) ? name : planId;
        state = state with { LoggedIn = loggedIn, Account = loggedIn ? account : null, Plan = loggedIn ? plan : null };

        string? problem;
        bool loginFix = true;
        if (!loggedIn) problem = "로그인이 필요해요.";
        else if (provider != null && provider != "firstParty") problem = "Claude가 아닌 다른 제공자로 설정되어 있어요.";
        else if (!console && planId == null) problem = "구독 정보가 없어요. 구독 계정이나 API로 다시 로그인해 주세요.";
        else if (!console && string.Equals(planId, "free", StringComparison.OrdinalIgnoreCase)) problem = "Free 플랜은 쓸 수 없어요. Pro 이상 구독 계정이나 API로 로그인해 주세요.";
        else if (tried.Result is { } failure) { problem = failure.Problem; loginFix = failure.Login; }
        else return state with { Method = console ? AuthMethod.Console : AuthMethod.Subscription };
        return state with { CanLogin = loginFix, Problem = problem };
    }

    private sealed record Failure(string Problem, bool Login, string? Reason);

    /// <summary>One tiny request on the cheapest model, no tools, nothing kept. Null when it worked.</summary>
    private static async Task<Failure?> TryRequest(string exe)
    {
        var answer = await ClaudeCli.Run(exe, new[]
        {
            "-p", "ok", "--model", "haiku", "--output-format", "json", "--tools", "",
            "--no-session-persistence", "--strict-mcp-config", "--system-prompt", "Reply with OK.",
        }, TimeSpan.FromSeconds(45), ProbeDir);
        JsonObject? result = null;
        try { result = JsonNode.Parse(answer.Stdout) as JsonObject; }
        catch { /* failed before answering */ }
        bool isError = result?["is_error"] is JsonValue v && v.TryGetValue(out bool e) && e;
        if (answer.Ok && result != null && !isError) return null;
        string reason = Str(result?["result"]) ?? (answer.Stderr.Length > 0 ? answer.Stderr : answer.Stdout);
        string firstLine = reason.Split('\n')[0].Trim();
        if (firstLine.Length > 80) firstLine = firstLine[..80] + "…";
        Log.Warn($"chat: test request failed: {firstLine}");
        if (ClaudeCli.IsLoginError(reason)) return new Failure("로그인이 만료됐어요.", true, firstLine);
        return new Failure("Claude에 접속하지 못했어요" + (firstLine.Length > 0 ? ": " + firstLine : "."), false, firstLine.Length > 0 ? firstLine : null);
    }

    /// <summary>Whether a saved login exists now (cheap; for watching a login in progress).</summary>
    public static async Task<bool> LoggedIn(string exe)
    {
        var status = await ClaudeCli.Run(exe, new[] { "auth", "status" }, TimeSpan.FromSeconds(20));
        try { return JsonNode.Parse(status.Stdout) is JsonObject o && o["loggedIn"] is JsonValue v && v.TryGetValue(out bool b) && b; }
        catch { return false; }
    }

    /// <summary>Whether this version's <c>auth login</c> takes a flag (older versions lack <c>--claudeai</c>/<c>--console</c>).</summary>
    public static async Task<bool> HasLoginFlag(string exe, string flag)
    {
        var help = await ClaudeCli.Run(exe, new[] { "auth", "login", "--help" }, TimeSpan.FromSeconds(20));
        return help.Stdout.Contains(flag, StringComparison.Ordinal);
    }

    /// <summary>Whether Claude Code runs (for watching an install in progress).</summary>
    public static async Task<bool> Runs(string exe) =>
        (await ClaudeCli.Run(exe, new[] { "--version" }, TimeSpan.FromSeconds(20))).Ok;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s) ? s : null;
}
