using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// What the chat keeps between game starts (sessions.json in the game's chat folder): the control session's
/// conversation id, each run's play session id (by run key) and whether that run ended, the brief the control session
/// left for the next run, and the last run's report waiting for the control session. Run reports are also kept as
/// files (reports/&lt;run key&gt;.md). Main thread only.
/// </summary>
internal sealed class SessionStore
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sts2-agent-mcp");

    private static string FilePath => Path.Combine(Dir, "sessions.json");

    private JsonObject _root = new();

    public static SessionStore Load()
    {
        var store = new SessionStore();
        try
        {
            if (File.Exists(FilePath)) store._root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: sessions.json unreadable, starting fresh: {ex.Message}");
        }
        return store;
    }

    public string? ControlId
    {
        get => Str(_root["control"]);
        set { _root["control"] = value; Save(); }
    }

    /// <summary>The play session id of a run, unless the run already ended (a replayed seed starts fresh).</summary>
    public string? PlayId(string runKey) =>
        Runs[runKey] is JsonObject run && !(run["ended"] is JsonValue e && e.TryGetValue(out bool ended) && ended)
            ? Str(run["id"])
            : null;

    public void SetPlayId(string runKey, string id)
    {
        Runs[runKey] = new JsonObject { ["id"] = id, ["ended"] = false, ["at"] = DateTime.Now.ToString("s") };
        Save();
    }

    public void MarkEnded(string runKey)
    {
        if (Runs[runKey] is JsonObject run) run["ended"] = true;
        else Runs[runKey] = new JsonObject { ["ended"] = true };
        Save();
    }

    /// <summary>What the control session wants the next run's play session to know.</summary>
    public string? Brief
    {
        get => Str(_root["brief"]);
        set { _root["brief"] = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Save(); }
    }

    /// <summary>The last run's report, until the control session has been given it.</summary>
    public string? PendingReport
    {
        get => Str(_root["report"]);
        set { _root["report"] = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Save(); }
    }

    public void SaveReport(string runKey, string text)
    {
        try
        {
            string dir = Path.Combine(Dir, "reports");
            Directory.CreateDirectory(dir);
            string safe = string.Concat(runKey.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllText(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmm}-{safe}.md"), text.Trim() + "\n", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: report not saved: {ex.Message}");
        }
    }

    /// <summary>After a logout: no conversation is resumed (the next login may be another account).</summary>
    public void ForgetSessions()
    {
        _root["control"] = null;
        _root["runs"] = new JsonObject();
        Save();
    }

    private JsonObject Runs
    {
        get
        {
            if (_root["runs"] is not JsonObject runs) _root["runs"] = runs = new JsonObject();
            return runs;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: sessions.json not saved: {ex.Message}");
        }
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s) ? s : null;
}
