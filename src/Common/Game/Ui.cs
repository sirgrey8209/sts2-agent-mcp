using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Sts2AgentMcp.Game;

/// <summary>A clickable control discovered on screen.</summary>
internal sealed record UiButton(string Key, string Label, NClickableControl Control);

/// <summary>Godot scene-tree helpers for UI-driven screens.</summary>
internal static class Ui
{
    /// <summary>The in-game chat panel's layer (STS2AgentChat); skipped when looking for the game's own nodes.</summary>
    public const string ChatPanelNodeName = "AgentChatPanel";

    public static Node Root => ((SceneTree)Engine.GetMainLoop()).Root;

    public static bool IsLive(Node? node)
    {
        try { return node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion(); }
        catch (ObjectDisposedException) { return false; }
    }

    public static bool IsVisible(CanvasItem? node)
    {
        try { return node != null && IsLive(node) && node.IsVisibleInTree(); }
        catch (ObjectDisposedException) { return false; }
    }

    public static List<T> FindAll<T>(Node? start) where T : Node
    {
        var found = new List<T>();
        if (IsLive(start)) Walk(start!, found);
        return found;
    }

    public static T? FindFirst<T>(Node? start) where T : Node => FindAll<T>(start).FirstOrDefault();

    /// <summary>Sorted top-to-bottom, left-to-right (child order is not reliable: focus moves nodes to front).</summary>
    public static List<T> FindAllByPosition<T>(Node? start) where T : Control
    {
        var list = FindAll<T>(start);
        list.Sort((a, b) =>
        {
            int cmp = Math.Round(a.GlobalPosition.Y).CompareTo(Math.Round(b.GlobalPosition.Y));
            return cmp != 0 ? cmp : a.GlobalPosition.X.CompareTo(b.GlobalPosition.X);
        });
        return list;
    }

    private static void Walk<T>(Node node, List<T> found) where T : Node
    {
        if (!IsLive(node)) return;
        // The in-game chat panel is not part of the game: its texts and buttons must not show up as the game's.
        if (node.Name == ChatPanelNodeName) return;
        if (node is T match) found.Add(match);
        try
        {
            foreach (var child in node.GetChildren()) Walk(child, found);
        }
        catch (ObjectDisposedException) { }
    }

    public static bool IsActionable(NClickableControl? control) =>
        control != null && control.IsEnabled && IsVisible(control);

    /// <summary>
    /// Buttons declared as fields named "_somethingButton" on a screen node (e.g. NMainMenu._continueButton).
    /// Keys come from the field name ("continue", "abandon_run"), so new buttons added by game updates appear automatically.
    /// </summary>
    public static List<UiButton> FieldButtons(object screen)
    {
        var result = new List<UiButton>();
        for (var type = screen.GetType(); type != null && type != typeof(Node); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!typeof(NClickableControl).IsAssignableFrom(field.FieldType)) continue;
                string name = field.Name.TrimStart('_');
                if (!name.EndsWith("Button", StringComparison.Ordinal) || name.Length <= "Button".Length) continue;
                if (field.GetValue(screen) is not NClickableControl control || !IsActionable(control)) continue;
                if (result.Any(b => ReferenceEquals(b.Control, control))) continue;
                string key = SnakeCase(name[..^"Button".Length]);
                result.Add(new UiButton(key, LabelOf(control) ?? key, control));
            }
        }
        return result;
    }

    /// <summary>All actionable clickable controls under a node, by screen position, excluding the top bar.</summary>
    public static List<UiButton> VisibleButtons(Node? start)
    {
        var result = new List<UiButton>();
        foreach (var control in FindAllByPosition<NClickableControl>(start))
        {
            if (!IsActionable(control) || IsInTopBar(control)) continue;
            string label = LabelOf(control) ?? control.Name.ToString();
            result.Add(new UiButton(result.Count.ToString(), label, control));
        }
        return result;
    }

    private static bool IsInTopBar(Node node)
    {
        for (var n = node; n != null; n = n.GetParent())
        {
            if (n.GetType().Name.Contains("TopBar", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static string? LabelOf(Node control)
    {
        foreach (var fieldName in new[] { "_label", "_textLabel", "_title", "_buttonLabel", "_text" })
        {
            var text = NodeText(GetField(control, fieldName));
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return FindText(control, 0);
    }

    /// <summary>All visible texts under a node (labels, rich text), in tree order, deduplicated.</summary>
    public static List<string> Texts(Node? start, int max = 20)
    {
        var texts = new List<string>();
        foreach (var control in FindAll<Control>(start))
        {
            if (texts.Count >= max) break;
            if (!IsVisible(control) || IsInsideClickable(control)) continue;
            var text = NodeText(control);
            if (!string.IsNullOrWhiteSpace(text) && !texts.Contains(text)) texts.Add(text);
        }
        return texts;
    }

    /// <summary>Button labels are listed with the buttons, so body text skips anything inside a clickable.</summary>
    private static bool IsInsideClickable(Node node)
    {
        for (var n = node; n != null; n = n.GetParent())
        {
            if (n is NClickableControl) return true;
        }
        return false;
    }

    private static string? FindText(Node node, int depth)
    {
        if (depth > 6 || !IsLive(node)) return null;
        var text = NodeText(node);
        if (!string.IsNullOrWhiteSpace(text)) return text;
        try
        {
            foreach (var child in node.GetChildren())
            {
                if (child is CanvasItem ci && !ci.Visible) continue;
                text = FindText(child, depth + 1);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch (ObjectDisposedException) { }
        return null;
    }

    private static string? NodeText(object? node)
    {
        if (node == null) return null;
        foreach (var prop in new[] { "Text", "BbcodeText" })
        {
            var value = Text.Of(() => node.GetType().GetProperty(prop)?.GetValue(node));
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    public static object? GetField(object source, string name)
    {
        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                try { return field.GetValue(source); }
                catch { return null; }
            }
        }
        return null;
    }

    public static string SnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
