using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Finding and starting the player's Claude Code (docs/in-game-chat.md §3.1), the way Windows would whichever way it
/// was installed: a native install leaves an .exe, npm leaves a .cmd wrapper that only starts through cmd.exe with its
/// arguments quoted. Every run gets the game's own config folder (its login and conversations, apart from any other
/// Claude Code use on the PC) and an environment without the marks of another Claude Code session.
/// </summary>
internal static class ClaudeCli
{
    private static readonly string[] Extensions = { ".exe", ".cmd", ".bat" };

    // What the child must not inherit: settings that bill another account or provider, and the marks of a Claude
    // Code session the game was started from (which would make the child run as that session's child).
    // The player's ANTHROPIC_API_KEY is never passed on: the API way is Claude Code's own Console login.
    private static readonly string[] ScrubbedEnv =
    {
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
        "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_SSE_PORT",
        "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_EXECPATH",
        "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN", "CLAUDE_PID", "CLAUDE_AGENT_SDK_VERSION",
        "CLAUDE_EFFORT", "CLAUDE_CONFIG_DIR",
    };

    private static readonly Regex LoginError =
        new(@"\b401\b|authenticat|oauth|token has expired|log ?in again|re-?auth|not logged in", RegexOptions.IgnoreCase);

    private static JobObject? _job;
    private static bool _jobTried;
    private static readonly object JobGate = new();

    /// <summary>The game's own Claude Code folder (login, settings, conversations).</summary>
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp", "claude-config");

    /// <summary>Where Anthropic's installer puts Claude Code on Windows.</summary>
    public static string NativeInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", OperatingSystem.IsWindows() ? "claude.exe" : "claude");

    public static bool IsLoginError(string? text) => text != null && LoginError.IsMatch(text);

    /// <summary>The configured path, then PATH, then the usual install folders; null if none.</summary>
    public static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        // Searched after PATH, so a stale PATH (installed after this Windows session began) still finds it.
        dirs.Add(Path.Combine(home, ".local", "bin"));                          // native installer
        dirs.Add(Path.Combine(appData, "npm"));                                  // npm install -g
        dirs.Add(Path.Combine(home, ".claude", "local"));                        // older per-user install
        dirs.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links"));    // winget
        foreach (var dir in dirs)
        {
            string clean = dir.Trim().Trim('"');
            if (clean.Length == 0) continue;
            if (!OperatingSystem.IsWindows())
            {
                string unix = Path.Combine(clean, "claude");
                if (File.Exists(unix)) return unix;
                continue;
            }
            foreach (var ext in Extensions)
            {
                string candidate = Path.Combine(clean, "claude" + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static bool NeedsShell(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".cmd" or ".bat";

    // For cmd.exe: every argument in double quotes, quotes inside doubled.
    private static string Quote(string arg) => "\"" + arg.Replace("\"", "\"\"") + "\"";

    /// <summary>A start info for Claude Code with the game's folder and a clean environment.</summary>
    public static ProcessStartInfo StartInfo(string exe, IEnumerable<string> args)
    {
        ProcessStartInfo psi;
        if (NeedsShell(exe))
        {
            // cmd /s /c "<line>": the outer quotes are stripped and the rest is run as it is.
            psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = "/d /s /c \"" + string.Join(" ", new[] { exe }.Concat(args).Select(Quote)) + "\"",
            };
        }
        else
        {
            psi = new ProcessStartInfo(exe);
            foreach (var arg in args) psi.ArgumentList.Add(arg);
        }
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        ApplyEnv(psi);
        return psi;
    }

    private static void ApplyEnv(ProcessStartInfo psi)
    {
        foreach (var key in psi.Environment.Keys.ToList())
            if (ScrubbedEnv.Contains(key, StringComparer.OrdinalIgnoreCase)) psi.Environment.Remove(key);
        Directory.CreateDirectory(ConfigDir);
        psi.Environment["CLAUDE_CONFIG_DIR"] = ConfigDir;
    }

    /// <summary>Puts a child in the job that dies with the game (even if the game is killed).</summary>
    public static void Adopt(Process process)
    {
        lock (JobGate)
        {
            if (!_jobTried) { _jobTried = true; _job = JobObject.TryCreate(); }
            _job?.Assign(process);
        }
    }

    public readonly record struct RunResult(bool Ok, int ExitCode, string Stdout, string Stderr);

    /// <summary>Runs Claude Code to its end, with a time limit. Never throws.</summary>
    public static async Task<RunResult> Run(string exe, IEnumerable<string> args, TimeSpan timeout, string? workDir = null)
    {
        Process? process = null;
        try
        {
            var psi = StartInfo(exe, args);
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            if (workDir != null) { Directory.CreateDirectory(workDir); psi.WorkingDirectory = workDir; }
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            Adopt(process);
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* gone */ }
                return new RunResult(false, -1, (await stdout).Trim(), "timed out");
            }
            return new RunResult(process.ExitCode == 0, process.ExitCode, (await stdout).Trim(), (await stderr).Trim());
        }
        catch (Exception ex)
        {
            return new RunResult(false, -1, "", ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Opens a console window of its own that runs the lines one after the other, for the player to use (install,
    /// login). It stays open at the end until a key is pressed. Not tied to the game's job: the player owns it.
    /// </summary>
    public static bool OpenConsole(string name, string title, params (string Exe, string[] Args)[] lines)
    {
        try
        {
            // A batch file instead of one nested command line: cmd's quoting of "start ... cmd /c "..."" leaves the
            // inside of each argument unquoted, so a '|' in an argument would become a pipe of the outer cmd.
            string dir = Path.GetDirectoryName(ConfigDir)!;
            Directory.CreateDirectory(dir);
            string script = Path.Combine(dir, name + ".cmd");
            var sb = new StringBuilder();
            sb.Append("@echo off\r\nchcp 65001 >nul\r\n");
            sb.Append("title ").Append(title.Replace("&", "and")).Append("\r\n");
            foreach (var (exe, args) in lines)
                sb.Append(string.Join(" ", new[] { exe }.Concat(args).Select(Quote))).Append("\r\n");
            sb.Append("echo.\r\npause\r\n");
            File.WriteAllText(script, sb.ToString(), new UTF8Encoding(false));

            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = $"/d /c start \"\" cmd /d /c {Quote(script)}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            ApplyEnv(psi);
            using var _ = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"chat: could not open console: {ex.Message}");
            return false;
        }
    }
}
