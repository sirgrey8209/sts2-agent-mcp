using Godot;

namespace Sts2AgentMcp.Host;

internal static class Log
{
    private const string Prefix = ModInfo.LogPrefix;

    public static void Info(string message) => GD.Print(Prefix + message);
    public static void Warn(string message) => GD.Print(Prefix + "WARN " + message);
    public static void Error(string message) => GD.PrintErr(Prefix + message);
}
