using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Claude Code's own login (<c>auth login</c>) without a window: Claude Code opens the browser itself and ends by
/// itself once the browser hands the sign-in back to its local callback. The sign-in address it prints is kept so the
/// card can open the browser again. Nothing is ever written to its input: a login code is never taken in by the mod,
/// so a sign-in that needs a pasted code goes to the console window instead (<see cref="ClaudeCli.OpenConsole"/>).
/// </summary>
internal sealed class LoginProcess
{
    private static readonly Regex UrlPattern = new(@"https://\S+", RegexOptions.Compiled);

    private readonly Process _process;
    private volatile bool _cancelled;
    private string? _lastError;

    private LoginProcess(Process process) => _process = process;

    /// <summary>The sign-in address Claude Code printed (null until it did).</summary>
    public string? Url { get; private set; }

    /// <summary>
    /// Starts the login. <paramref name="onUrl"/> gets the sign-in address once; <paramref name="onExit"/> gets the exit
    /// code and the last error line when the process ends (not after <see cref="Cancel"/>). Both run on a worker thread.
    /// Null when the process could not start.
    /// </summary>
    public static LoginProcess? Start(string exe, string[] args, Action<string> onUrl, Action<int, string?> onExit)
    {
        try
        {
            var psi = ClaudeCli.StartInfo(exe, args);
            psi.RedirectStandardInput = true; // kept open and empty: closing it could read as "no code"
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            ClaudeCli.Adopt(process);
            var login = new LoginProcess(process);
            login.Watch(onUrl, onExit);
            return login;
        }
        catch (Exception ex)
        {
            Log.Error($"chat: could not start login: {ex.Message}");
            return null;
        }
    }

    private void Watch(Action<string> onUrl, Action<int, string?> onExit)
    {
        var stdout = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
            {
                if (Url != null || UrlPattern.Match(line) is not { Success: true } match) continue;
                Url = match.Value;
                onUrl(Url);
            }
        });
        var stderr = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync()) != null)
                if (line.Trim().Length > 0) _lastError = line.Trim();
        });
        Task.Run(async () =>
        {
            int code = -1;
            try
            {
                await _process.WaitForExitAsync();
                try { await Task.WhenAll(stdout, stderr); } catch { /* pipes closed */ }
                code = _process.ExitCode;
            }
            catch (Exception ex) { _lastError ??= ex.Message; }
            Log.Info($"chat: login process ended ({code}){(_cancelled ? " after cancel" : "")}{(_lastError != null ? ": " + _lastError : "")}");
            if (!_cancelled) onExit(code, _lastError);
            _process.Dispose();
        });
    }

    /// <summary>Ends the login (the player cancelled, chose the console window, or the wait ran out).</summary>
    public void Cancel()
    {
        _cancelled = true;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* gone */ }
    }
}
