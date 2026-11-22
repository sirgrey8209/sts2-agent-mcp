using System;
using System.Collections.Generic;
using System.Linq;

namespace Sts2AgentMcp.Host;

/// <summary>
/// Isolates game-internal features. When a game update removes or changes an API, the .NET runtime throws
/// MissingMethodException / MissingFieldException / TypeLoadException the first time the calling method is JIT-compiled.
/// Keeping each feature in its own method and running it through <see cref="Try"/> means only that feature is disabled.
/// </summary>
internal static class Features
{
    private static readonly Dictionary<string, string> Disabled = new();

    public static IReadOnlyDictionary<string, string> DisabledFeatures => Disabled;

    public static bool IsEnabled(string feature) => !Disabled.ContainsKey(feature);

    public static bool Try(string feature, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (IsBindingFailure(ex))
        {
            Disable(feature, ex);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"feature '{feature}' failed: {ex}");
            return false;
        }
    }

    public static void Disable(string feature, Exception ex)
    {
        Disabled[feature] = $"{ex.GetType().Name}: {ex.Message}";
        Log.Error($"feature '{feature}' disabled (game API mismatch?): {ex}");
    }

    public static bool IsBindingFailure(Exception ex) =>
        ex is MissingMemberException or TypeLoadException or BadImageFormatException
        || (ex is TypeInitializationException tie && tie.InnerException != null && IsBindingFailure(tie.InnerException));

    public static string Summary() =>
        Disabled.Count == 0 ? "" : "disabled: " + string.Join(", ", Disabled.Keys.OrderBy(k => k));
}
