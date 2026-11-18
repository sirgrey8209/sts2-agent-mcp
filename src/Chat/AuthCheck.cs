using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

internal enum AuthMethod { None, Subscription, ApiKey }

/// <summary>
/// What the chat knows about the player's Claude Code: installed or not, the account and plan, and how the chat will
/// run (subscription first, then the player's own API key). <see cref="Problem"/> says what stands in the way;
/// <see cref="CanLogin"/>/<see cref="CanInstall"/> say which button fixes it.
/// </summary>
internal sealed record AuthState
{
    public bool Checking { get; init; }
    public bool Installed { get; init; }
    public string? Exe { get; init; }
    public string? Version { get; init; }
    public string? Account { get; init; }
    public string? Plan { get; init; }
    public AuthMethod Method { get; init; }
    public string? Problem { get; init; }
    public bool CanLogin { get; init; }
    public bool CanInstall { get; init; }

    public bool Ready => !Checking && Method != AuthMethod.None && Exe != null;

    /// <summary>The key the chat runs on (ApiKey method only).</summary>
    public string? ApiKey => Method == AuthMethod.ApiKey ? ClaudeCli.PlayerApiKey : null;

    /// <summary>Short label for the panel header.</summary>
    public string Label => Method switch
    {
        AuthMethod.Subscription => Plan != null ? $"구독({Plan})" : "구독",
        AuthMethod.ApiKey => "API 키",
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
/// logged in. Then the player's own <c>ANTHROPIC_API_KEY</c>, if the subscription does not work.
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

        var version = ClaudeCli.Run(exe, new[] { "--version" }, null, TimeSpan.FromSeconds(20));
        var status = ClaudeCli.Run(exe, new[] { "auth", "status" }, null, TimeSpan.FromSeconds(20));
        var tried = TryRequest(exe, apiKey: null);
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
        string? plan = planId == null ? null : PlanNames.TryGetValue(planId, out var name) ? name : planId;
        state = state with { Account = loggedIn ? account : null, Plan = loggedIn ? plan : null };

        // Subscription first.
        string? subProblem;
        bool subLoginFix = true;
        if (!loggedIn) subProblem = "로그인이 필요해요.";
        else if (provider != null && provider != "firstParty") subProblem = "Claude 구독이 아닌 다른 제공자로 설정되어 있어요.";
        else if (planId == null) subProblem = "구독 정보가 없어요. Claude 구독 계정으로 로그인해 주세요.";
        else if (planId.Equals("free", StringComparison.OrdinalIgnoreCase)) subProblem = "Free 플랜은 쓸 수 없어요. Pro 이상 구독 계정으로 로그인해 주세요.";
        else if (tried.Result is { } failure) { subProblem = failure.Problem; subLoginFix = failure.Login; }
        else return state with { Method = AuthMethod.Subscription };

        // Then the player's own key.
        if (ClaudeCli.PlayerApiKey is { } key)
        {
            var keyTry = await TryRequest(exe, key);
            if (keyTry == null) return state with { Method = AuthMethod.ApiKey, Plan = "API 키" };
            return state with
            {
                CanLogin = subLoginFix,
                Problem = subProblem + " API 키(ANTHROPIC_API_KEY)로도 접속하지 못했어요" + (keyTry.Reason is { } r ? ": " + r : "."),
            };
        }
        return state with { CanLogin = subLoginFix, Problem = subProblem };
    }

    private sealed record Failure(string Problem, bool Login, string? Reason);

    /// <summary>One tiny request on the cheapest model, no tools, nothing kept. Null when it worked.</summary>
    private static async Task<Failure?> TryRequest(string exe, string? apiKey)
    {
        var answer = await ClaudeCli.Run(exe, new[]
        {
            "-p", "ok", "--model", "haiku", "--output-format", "json", "--tools", "",
            "--no-session-persistence", "--strict-mcp-config", "--system-prompt", "Reply with OK.",
        }, apiKey, TimeSpan.FromSeconds(45), ProbeDir);
        JsonObject? result = null;
        try { result = JsonNode.Parse(answer.Stdout) as JsonObject; }
        catch { /* failed before answering */ }
        bool isError = result?["is_error"] is JsonValue v && v.TryGetValue(out bool e) && e;
        if (answer.Ok && result != null && !isError) return null;
        string reason = Str(result?["result"]) ?? (answer.Stderr.Length > 0 ? answer.Stderr : answer.Stdout);
        string firstLine = reason.Split('\n')[0].Trim();
        if (firstLine.Length > 80) firstLine = firstLine[..80] + "…";
        Log.Warn($"chat: test request failed ({(apiKey != null ? "api key" : "subscription")}): {firstLine}");
        if (ClaudeCli.IsLoginError(reason)) return new Failure("로그인이 만료됐어요.", true, firstLine);
        return new Failure("Claude에 접속하지 못했어요" + (firstLine.Length > 0 ? ": " + firstLine : "."), false, firstLine.Length > 0 ? firstLine : null);
    }

    /// <summary>Whether a saved login exists now (cheap; for watching a login in progress).</summary>
    public static async Task<bool> LoggedIn(string exe)
    {
        var status = await ClaudeCli.Run(exe, new[] { "auth", "status" }, null, TimeSpan.FromSeconds(20));
        try { return JsonNode.Parse(status.Stdout) is JsonObject o && o["loggedIn"] is JsonValue v && v.TryGetValue(out bool b) && b; }
        catch { return false; }
    }

    /// <summary>Whether this version's <c>auth login</c> takes <c>--claudeai</c> (older versions do not have it).</summary>
    public static async Task<bool> HasClaudeAiFlag(string exe)
    {
        var help = await ClaudeCli.Run(exe, new[] { "auth", "login", "--help" }, null, TimeSpan.FromSeconds(20));
        return help.Stdout.Contains("--claudeai", StringComparison.Ordinal);
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s) ? s : null;
}
