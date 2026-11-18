using System;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Game assets for the chat panel, taken from the Ancient dialogue line (scenes/events/ancient_dialogue_line.tscn):
/// the dialogue nine-patch bubble and tail, the dialogue text style, and character/ancient icons and dialogue colors.
/// Every asset has a plain fallback so a game update that moves a file only changes the look.
/// </summary>
internal static class ChatTheme
{
    public const int FontSize = 24;
    public const int SmallFontSize = 18;

    // Ancient dialogue line values (NinePatch margins, tail size and overlap, text margins, shadow offsets).
    public const int PatchH = 27, PatchV = 28;
    public const float TailWidth = 28, TailOverlap = 12;
    public const int TextMarginLeft = 20, TextMarginTop = 10, TextMarginRight = 18, TextMarginBottom = 12;
    public static readonly Vector2 BubbleShadowOffset = new(2, 5);
    public static readonly Color ShadowColor = new(0, 0, 0, 0.251f);
    public static readonly Color TextColor = new(1f, 0.9647f, 0.8863f, 1f);
    public static readonly Color TextShadow = new(0, 0, 0, 0.251f);

    public static readonly Color PanelColor = new(0.07f, 0.08f, 0.10f, 1f);
    public static readonly Color HeaderColor = new(0.10f, 0.12f, 0.15f, 1f);
    public static readonly Color NoteColor = new(0.85f, 0.76f, 0.52f, 0.85f);
    public static readonly Color ErrorColor = new(0.95f, 0.45f, 0.40f, 1f);
    public static readonly Color DimColor = new(0.70f, 0.70f, 0.72f, 1f);

    /// <summary>Neow's dialogue color (the agent speaks as the run's guide).</summary>
    public static readonly Color AgentColor = new("28454F");
    public static readonly Color PlayerFallbackColor = new("3A332B");

    private static Texture2D? _bubble, _tail, _agentIcon, _agentOutline;
    private static Font? _regular, _bold, _italic;
    private static bool _loaded;

    public static Texture2D? Bubble { get { Load(); return _bubble; } }
    public static Texture2D? Tail { get { Load(); return _tail; } }
    public static Texture2D? AgentIcon { get { Load(); return _agentIcon; } }
    public static Texture2D? AgentOutline { get { Load(); return _agentOutline; } }
    public static Font? Regular { get { Load(); return _regular; } }
    public static Font? Bold { get { Load(); return _bold; } }
    public static Font? Italic { get { Load(); return _italic; } }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _bubble = Tex("res://images/ui/dialogue_nine_patch.png");
        _tail = Tex("res://images/ui/dialogue_tail.png");
        _agentIcon = Tex("res://images/ui/run_history/neow.png");
        _agentOutline = Tex("res://images/ui/run_history/neow_outline.png");
        Features.Try("chat-fonts", () =>
        {
            string language = LocManager.Instance?.Language ?? "eng";
            _regular = FontManager.GetSubstituteFont(language, FontType.Regular) ?? FontRes("res://themes/kreon_regular_glyph_space_one.tres");
            _bold = FontManager.GetSubstituteFont(language, FontType.Bold) ?? FontRes("res://themes/kreon_bold_glyph_space_one.tres");
            _italic = FontManager.GetSubstituteFont(language, FontType.Italic) ?? FontRes("res://themes/bitter_medium_italic_glyph_space_one.tres");
        });
    }

    private static Texture2D? Tex(string path)
    {
        try { return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null; }
        catch (Exception ex) { Log.Warn($"chat theme: {path}: {ex.Message}"); return null; }
    }

    private static Font? FontRes(string path)
    {
        try { return ResourceLoader.Exists(path) ? ResourceLoader.Load<Font>(path) : null; }
        catch (Exception ex) { Log.Warn($"chat theme: {path}: {ex.Message}"); return null; }
    }

    /// <summary>Main thread. The local player's character icon, outline and dialogue color (null icons outside a run).</summary>
    public static (Texture2D? Icon, Texture2D? Outline, Color Color) Player()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress) return (null, null, PlayerFallbackColor);
            var me = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            var character = me?.Character;
            if (character == null) return (null, null, PlayerFallbackColor);
            return (character.IconTexture, character.IconOutlineTexture, character.DialogueColor);
        }
        catch (Exception ex)
        {
            Log.Warn($"chat theme: player icon: {ex.Message}");
            return (null, null, PlayerFallbackColor);
        }
    }

    /// <summary>A text label in the game's dialogue style.</summary>
    public static void StyleText(Control label, int size = FontSize, Color? color = null)
    {
        if (label is RichTextLabel rich)
        {
            if (Regular != null) rich.AddThemeFontOverride("normal_font", Regular);
            if (Bold != null) rich.AddThemeFontOverride("bold_font", Bold);
            if (Italic != null) rich.AddThemeFontOverride("italics_font", Italic);
            foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size" })
                rich.AddThemeFontSizeOverride(key, size);
            rich.AddThemeColorOverride("default_color", color ?? TextColor);
            rich.AddThemeColorOverride("font_shadow_color", TextShadow);
            rich.AddThemeConstantOverride("shadow_offset_x", 3);
            rich.AddThemeConstantOverride("shadow_offset_y", 2);
            rich.AddThemeConstantOverride("line_separation", -2);
        }
        else
        {
            if (Regular != null) label.AddThemeFontOverride("font", Regular);
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", color ?? TextColor);
            label.AddThemeColorOverride("font_shadow_color", TextShadow);
            label.AddThemeConstantOverride("shadow_offset_x", 2);
            label.AddThemeConstantOverride("shadow_offset_y", 2);
        }
    }

    /// <summary>Input box: the dialogue bubble, darkened.</summary>
    public static StyleBox InputStyle(bool focused)
    {
        var color = focused ? new Color(0.20f, 0.30f, 0.34f, 1f) : new Color(0.13f, 0.17f, 0.20f, 1f);
        if (Bubble != null)
        {
            return new StyleBoxTexture
            {
                Texture = Bubble,
                TextureMarginLeft = PatchH, TextureMarginRight = PatchH, TextureMarginTop = PatchV, TextureMarginBottom = PatchV,
                ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 12, ContentMarginBottom = 12,
                ModulateColor = color,
            };
        }
        return new StyleBoxFlat
        {
            BgColor = color, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 12, ContentMarginBottom = 12,
        };
    }

    public static readonly Color OkColor = new(0.55f, 0.85f, 0.55f, 1f);
    public static readonly Color WaitColor = new(0.85f, 0.76f, 0.52f, 1f);
    /// <summary>Claude's orange (#D97757, the color its brand pages use).</summary>
    public static readonly Color ClaudeColor = new("D97757");
    /// <summary>Codex blue (the deep end of the Codex logo's gradient, #3941FF).</summary>
    public static readonly Color CodexColor = new("3941FF");
    /// <summary>Usable but billed per use (API key).</summary>
    public static readonly Color WarnColor = new(0.98f, 0.82f, 0.30f, 1f);
    public static readonly Color PrimaryButton = new(0.18f, 0.36f, 0.42f, 1f);
    public static readonly Color PlainButton = new(0.16f, 0.19f, 0.23f, 1f);
    public static readonly Color DangerButton = new(0.42f, 0.18f, 0.16f, 1f);

    /// <summary>Login card: the dialogue bubble, dark.</summary>
    public static StyleBox CardStyle() => Box(new Color(0.12f, 0.15f, 0.18f, 1f), 30, 24);

    /// <summary>A button in the bubble look: normal, hover (lighter), pressed (darker), disabled (dim).</summary>
    public static void StyleButton(Button button, Color color, int fontSize = 22)
    {
        button.AddThemeStyleboxOverride("normal", Box(color, 22, 8));
        button.AddThemeStyleboxOverride("hover", Box(color.Lightened(0.18f), 22, 8));
        button.AddThemeStyleboxOverride("pressed", Box(color.Darkened(0.2f), 22, 8));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("disabled", Box(color.Darkened(0.4f), 22, 8));
        if (Regular != null) button.AddThemeFontOverride("font", Regular);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        foreach (var key in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(key, TextColor);
        button.AddThemeColorOverride("font_disabled_color", new Color(TextColor, 0.4f));
        button.AddThemeColorOverride("font_shadow_color", TextShadow);
        button.AddThemeConstantOverride("shadow_offset_x", 2);
        button.AddThemeConstantOverride("shadow_offset_y", 2);
        button.FocusMode = Control.FocusModeEnum.None;
    }

    private static StyleBox Box(Color color, int marginH, int marginV)
    {
        if (Bubble != null)
        {
            return new StyleBoxTexture
            {
                Texture = Bubble,
                TextureMarginLeft = PatchH, TextureMarginRight = PatchH, TextureMarginTop = PatchV, TextureMarginBottom = PatchV,
                ContentMarginLeft = marginH, ContentMarginRight = marginH, ContentMarginTop = marginV, ContentMarginBottom = marginV,
                ModulateColor = color,
            };
        }
        return new StyleBoxFlat
        {
            BgColor = color, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = marginH, ContentMarginRight = marginH, ContentMarginTop = marginV, ContentMarginBottom = marginV,
        };
    }

    /// <summary>Plain chat text (markdown-ish) to game BBCode: escape brackets, **bold**, bullets; headings and code marks dropped.</summary>
    public static string ToBbcode(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#")) line = trimmed.TrimStart('#').TrimStart();
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) line = "• " + trimmed[2..];
            if (trimmed.StartsWith("```")) continue;
            line = line.Replace("[", "[lb]").Replace("`", "");
            // **bold** pairs
            var parts = line.Split("**");
            for (int i = 0; i < parts.Length; i++)
                sb.Append(i % 2 == 1 && i < parts.Length - 1 ? "[b]" + parts[i] + "[/b]" : (i % 2 == 1 ? "**" + parts[i] : parts[i]));
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Text as displayed (for measuring): markdown marks removed.</summary>
    public static string ToPlain(string text) =>
        text.Replace("\r", "").Replace("**", "").Replace("`", "").Replace("```", "");
}
