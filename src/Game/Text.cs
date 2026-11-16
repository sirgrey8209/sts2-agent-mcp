using System;
using System.Text;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2AgentMcp.Game;

/// <summary>Game text helpers: safe access, LocString formatting, rich-text tag stripping.</summary>
internal static class Text
{
    /// <summary>Evaluates a getter and returns its text (LocString formatted, tags stripped). Never throws.</summary>
    public static string? Of(Func<object?> getter)
    {
        try
        {
            var value = getter();
            return value switch
            {
                null => null,
                LocString loc => Clean(loc.GetFormattedText()),
                string s => Clean(s),
                _ => Clean(value.ToString() ?? ""),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strips BBCode-style tags and collapses newlines. [img]res://.../x.png[/img] becomes [x].</summary>
    public static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '[')
            {
                if (text.AsSpan(i).StartsWith("[img]"))
                {
                    int start = i + 5;
                    int close = text.IndexOf("[/img]", start, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        string path = text[start..close];
                        int slash = path.LastIndexOf('/');
                        string file = slash >= 0 ? path[(slash + 1)..] : path;
                        int dot = file.LastIndexOf('.');
                        sb.Append('[').Append(dot > 0 ? file[..dot] : file).Append(']');
                        i = close + 6;
                        continue;
                    }
                }
                int end = text.IndexOf(']', i);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(c is '\n' or '\r' ? ' ' : c);
            i++;
        }
        // collapse repeated spaces
        var result = sb.ToString();
        while (result.Contains("  ")) result = result.Replace("  ", " ");
        return result.Trim();
    }
}
