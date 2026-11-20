using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// An agent message turned into chat BBCode (docs/chat-roadmap.md step 3). Markdown-ish marks as in
/// <see cref="ChatTheme.ToBbcode"/>; text in backticks names a card or relic: a combat id (c3), a game id (BASH;
/// BASH+ for the upgraded card) or the name shown in the game (발놀림, 발놀림+). It shows as the name, in gold, with
/// the game's tooltip on hover. Other text in backticks shows as plain text, ids inside it still becoming names
/// (`c3 → c5` reads 발놀림+ → 탈출구). Built on the main thread when the message arrives, while combat ids still
/// resolve. Names are links <c>[url=t&lt;tip&gt;]</c>.
/// </summary>
internal sealed class Markup
{
    private static readonly Regex IdToken = new(@"(?<![A-Za-z0-9_])(c\d+|[A-Z][A-Z0-9_]{2,}\+?)(?![A-Za-z0-9_])", RegexOptions.Compiled);
    private const string NameFg = "#f2c96b";

    public string Bbcode { get; private set; } = "";
    public string Plain { get; private set; } = "";
    /// <summary>Tooltip entries of each name (text, used when the game's tooltip cannot be shown).</summary>
    public List<List<(string Title, string Body)>> Tips { get; } = new();
    /// <summary>The game's own hover tips of each name (card picture with its keywords, relic tip).</summary>
    public List<List<IHoverTip>> GameTips { get; } = new();

    public static Markup Parse(string text)
    {
        var m = new Markup();
        var bb = new StringBuilder();
        var plain = new StringBuilder();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```")) continue;
            if (trimmed.StartsWith("#")) line = trimmed.TrimStart('#').TrimStart();
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) line = "• " + trimmed[2..];

            var parts = line.Split('`').ToList();
            if (parts.Count % 2 == 0)
            {
                // An unmatched backtick stays a plain character.
                parts[^2] = parts[^2] + "`" + parts[^1];
                parts.RemoveAt(parts.Count - 1);
            }
            string? shownBefore = null; // the name a backtick part ended with, for the particle after it
            for (int i = 0; i < parts.Count; i++)
            {
                if (i % 2 == 0)
                {
                    string piece = shownBefore != null ? Particles.Fit(shownBefore, parts[i]) : parts[i];
                    bb.Append(Bold(Escape(ChatTheme.KeepAll(piece))));
                    plain.Append(piece.Replace("**", ""));
                }
                else
                {
                    shownBefore = m.AddCode(parts[i], bb, plain);
                }
            }
            bb.Append('\n');
            plain.Append('\n');
        }
        m.Bbcode = bb.ToString().TrimEnd('\n');
        m.Plain = plain.ToString().TrimEnd('\n');
        return m;
    }

    /// <summary>Text in backticks: one card or relic as a whole, else plain text with any ids in it as names.
    /// Returns the name it ends with (the particle after it is fitted to it), or null.</summary>
    private string? AddCode(string code, StringBuilder bb, StringBuilder plain)
    {
        string whole = code.Trim();
        if ((Resolve(whole) ?? ResolveName(whole)) is { } named)
        {
            AddName(named);
            return named.Name;
        }
        int at = 0;
        string? last = null;
        foreach (Match match in IdToken.Matches(code))
        {
            AppendText(code[at..match.Index]);
            last = null;
            if (Resolve(match.Value) is { } resolved)
            {
                AddName(resolved);
                last = resolved.Name;
            }
            else AppendText(match.Value);
            at = match.Index + match.Length;
        }
        if (at < code.Length) last = null;
        AppendText(code[at..]);
        return last;

        void AppendText(string piece)
        {
            if (piece.Length == 0) return;
            piece = piece.Replace("->", "→");
            bb.Append(Escape(ChatTheme.KeepAll(piece)));
            plain.Append(piece);
        }

        void AddName((string Name, List<(string, string)> Tips, List<IHoverTip> GameTips) r)
        {
            int k = Tips.Count;
            Tips.Add(r.Tips);
            GameTips.Add(r.GameTips);
            bb.Append($"[url=t{k}][color={NameFg}]{Escape(ChatTheme.KeepAll(r.Name))}[/color][/url]");
            plain.Append(r.Name);
        }
    }

    /// <summary>A card (combat id or game id, "+" for upgraded) or relic id: its name and tooltip; null otherwise.</summary>
    private static (string Name, List<(string, string)> Tips, List<IHoverTip> GameTips)? Resolve(string token)
    {
        try
        {
            if (!IdToken.IsMatch(token) || IdToken.Match(token).Length != token.Length) return null;
            if (token.Length > 1 && token[0] == 'c' && char.IsDigit(token[1]))
            {
                var card = Ids.ResolveCard(token);
                return card == null ? null : (Fmt.CardLabel(card), CardTip(card), CardGameTips(card));
            }
            bool upgraded = token.EndsWith('+');
            string id = token.TrimEnd('+');
            switch (Catalog.FindById(id))
            {
                case CardModel card:
                {
                    var shown = upgraded ? Upgraded(card) ?? card : card;
                    return (Fmt.CardName(card) + (upgraded ? "+" : ""), CardTip(shown), CardGameTips(shown));
                }
                case RelicModel relic:
                    return (Text.Of(() => relic.Title) ?? relic.Id.Entry, RelicTip(relic), RelicGameTips(relic));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: id {token} not shown as a name: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// A card or relic by the name shown in the game: the player's own first (this fight's piles, the deck, the relics,
    /// so the tooltip has their current numbers), then the game's catalog ("+" for the upgraded card).
    /// </summary>
    private static (string Name, List<(string, string)> Tips, List<IHoverTip> GameTips)? ResolveName(string name)
    {
        if (name.Length == 0 || name.Length > 40) return null;
        try
        {
            var ctx = GameCtx.Capture();
            var me = ctx.Me;
            if (me != null)
            {
                IEnumerable<CardModel> owned = me.Deck.Cards;
                var pcs = me.PlayerCombatState;
                if (ctx.InCombat && pcs != null)
                    owned = pcs.Hand.Cards.Concat(pcs.DrawPile.Cards).Concat(pcs.DiscardPile.Cards).Concat(pcs.ExhaustPile.Cards).Concat(owned);
                var card = owned.FirstOrDefault(c => Fmt.CardName(c) == name);
                if (card != null) return (Fmt.CardLabel(card), CardTip(card), CardGameTips(card));
                var relic = me.Relics.FirstOrDefault(r => Text.Of(() => r.Title) == name);
                if (relic != null) return (name, RelicTip(relic), RelicGameTips(relic));
            }
            bool upgraded = name.EndsWith('+');
            switch (Catalog.FindByTitle(name.TrimEnd('+')))
            {
                case CardModel card:
                {
                    var shown = upgraded ? Upgraded(card) ?? card : card;
                    return (Fmt.CardName(card) + (upgraded ? "+" : ""), CardTip(shown), CardGameTips(shown));
                }
                case RelicModel relic when !upgraded:
                    return (name, RelicTip(relic), RelicGameTips(relic));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: name {name} not shown as a card: {ex.Message}");
        }
        return null;
    }

    /// <summary>An upgraded copy of a card (to show "BASH+"), or null when it cannot be upgraded.</summary>
    private static CardModel? Upgraded(CardModel card)
    {
        try
        {
            if (!card.IsUpgradable) return null;
            var copy = card.IsMutable ? (CardModel)card.MutableClone() : card.ToMutable();
            copy.UpgradeInternal();
            return copy;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The game's tooltip of a card: the card itself, then its keywords.</summary>
    public static List<IHoverTip> CardGameTips(CardModel card)
    {
        var tips = new List<IHoverTip> { new CardHoverTip(card) };
        tips.AddRange(Safe(() => card.HoverTips.ToList()) ?? new List<IHoverTip>());
        return tips;
    }

    public static List<IHoverTip> RelicGameTips(RelicModel relic) => Safe(() => relic.HoverTips.ToList()) ?? new List<IHoverTip>();

    public static List<(string Title, string Body)> CardTip(CardModel card)
    {
        var pile = card.Pile?.Type ?? PileType.None;
        var entries = new List<(string, string)>
        {
            ($"{Fmt.CardName(card)} [{Fmt.Cost(card)}]", Fmt.CardDescription(card, pile)),
        };
        if (!card.IsUpgraded && Fmt.UpgradedDescription(card) is { } upgraded)
            entries.Add(("강화하면", upgraded));
        entries.AddRange(HoverTips(Safe(() => card.HoverTips.ToList()) ?? new List<IHoverTip>()));
        return entries;
    }

    public static List<(string Title, string Body)> RelicTip(RelicModel relic)
    {
        var entries = HoverTips(Safe(() => relic.HoverTips.ToList()) ?? new List<IHoverTip>());
        if (entries.Count == 0) entries.Add((Text.Of(() => relic.Title) ?? relic.Id.Entry, Text.Of(() => relic.DynamicDescription) ?? ""));
        return entries;
    }

    private static List<(string Title, string Body)> HoverTips(IEnumerable<IHoverTip> tips)
    {
        var result = new List<(string, string)>();
        foreach (var tip in tips)
        {
            switch (tip)
            {
                case HoverTip h:
                    result.Add((Text.Clean(h.Title ?? ""), Text.Clean(h.Description ?? "")));
                    break;
                case CardHoverTip c:
                    result.Add((Fmt.CardName(c.Card), Fmt.CardDescription(c.Card, PileType.None)));
                    break;
            }
        }
        return result.Where(e => e.Item1.Length > 0 || e.Item2.Length > 0).Distinct().ToList();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    /// <summary>**bold** pairs on one piece of a line.</summary>
    private static string Bold(string text)
    {
        var parts = text.Split("**");
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
            sb.Append(i % 2 == 1 && i < parts.Length - 1 ? "[b]" + parts[i] + "[/b]" : (i % 2 == 1 ? "**" + parts[i] : parts[i]));
        return sb.ToString();
    }

    private static T? Safe<T>(Func<T?> get) where T : class
    {
        try { return get(); } catch { return null; }
    }
}

/// <summary>Chat panel: names with tooltips, cards and relics shown in the game's look, tooltips.</summary>
internal sealed partial class ChatPanel
{
    private const float CardWidth = 150;
    private const float RelicSize = 64;
    private const float TipWidth = 380;

    private PanelContainer? _tip;
    private Control? _gameTipOwner;

    // ---- names in agent messages ----

    private void OnMetaHover(Markup markup, string meta, bool on, Control label, Vector2 mouse)
    {
        if (!on)
        {
            HideTip();
            return;
        }
        if (!TryIndex(meta, 't', out int tip) || tip >= markup.Tips.Count) return;
        // Right of the chat panel, level with the hovered name.
        float y = mouse.Y - 20;
        if (!ShowGameTip(label, y, markup.GameTips[tip])) ShowTip(y, markup.Tips[tip]);
    }

    private static bool TryIndex(string meta, char prefix, out int index)
    {
        index = -1;
        if (meta.Length < 2 || meta[0] != prefix) return false;
        int end = meta.IndexOf(':');
        return int.TryParse(end < 0 ? meta[1..] : meta[1..end], out index);
    }

    // ---- cards and relics ----

    public void AddCards(IReadOnlyList<CardModel> cards)
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        float height = CardWidth * NCard.defaultSize.Y / NCard.defaultSize.X;
        foreach (var card in cards)
        {
            var slot = new TextureRect
            {
                CustomMinimumSize = new Vector2(CardWidth, height),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            var entries = Markup.CardTip(card);
            var gameTips = Markup.CardGameTips(card);
            slot.MouseEntered += () =>
            {
                float y = slot.GetGlobalRect().Position.Y;
                if (!ShowGameTip(slot, y, gameTips)) ShowTip(y, entries);
            };
            slot.MouseExited += HideTip;
            flow.AddChild(slot);
            RenderCard(card, slot);
        }
        Append(Indented(flow));
    }

    public void AddRelics(IReadOnlyList<RelicModel> relics)
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var relic in relics)
        {
            Texture2D? icon = null;
            try { icon = relic.Icon; } catch (Exception ex) { Log.Warn($"chat: relic icon {relic.Id}: {ex.Message}"); }
            var slot = new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(RelicSize, RelicSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            var entries = Markup.RelicTip(relic);
            var gameTips = Markup.RelicGameTips(relic);
            slot.MouseEntered += () =>
            {
                float y = slot.GetGlobalRect().Position.Y;
                if (!ShowGameTip(slot, y, gameTips)) ShowTip(y, entries);
            };
            slot.MouseExited += HideTip;
            flow.AddChild(slot);
        }
        Append(Indented(flow));
    }

    /// <summary>Lines up with the agent's bubbles (past the icon).</summary>
    private static MarginContainer Indented(Control content)
    {
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", (int)(IconSize + 8));
        margin.AddChild(content);
        return margin;
    }

    /// <summary>Draws the card with the game's card node off screen and puts the picture in <paramref name="target"/>.</summary>
    private static async void RenderCard(CardModel card, TextureRect target)
    {
        SubViewport? viewport = null;
        NCard? node = null;
        try
        {
            (viewport, node) = await MainThread.Run(() =>
            {
                var size = NCard.defaultSize;
                var vp = new SubViewport
                {
                    Size = new Vector2I((int)size.X, (int)size.Y),
                    TransparentBg = true,
                    Disable3D = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                };
                Ui.Root.AddChild(vp);
                var model = card.IsMutable ? card : card.ToMutable();
                var n = NCard.Create(model);
                if (n != null)
                {
                    vp.AddChild(n);
                    n.Visible = true;
                    n.Rotation = 0;
                    n.Scale = Vector2.One;
                    n.Position = size / 2; // NCard draws centered on its position
                    try { n.UpdateVisuals(card.Pile?.Type ?? PileType.None, CardPreviewMode.Normal); }
                    catch (Exception ex) { Log.Warn($"chat: card visuals failed for {model.Id}: {ex.Message}"); }
                }
                return (vp, n);
            });
            int frames = 0;
            await MainThread.Poll(() => ++frames >= 4 ? "ok" : null, TimeSpan.FromSeconds(5), () => "timeout");
            await MainThread.Run(() =>
            {
                if (GodotObject.IsInstanceValid(target))
                    target.Texture = ImageTexture.CreateFromImage(viewport!.GetTexture().GetImage());
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: card picture failed: {ex.Message}");
        }
        finally
        {
            var vp = viewport;
            var n = node;
            if (vp != null)
            {
                _ = MainThread.Run(() =>
                {
                    if (n != null)
                    {
                        try { vp.RemoveChild(n); NodePool.Free(n); }
                        catch (Exception ex) { Log.Warn($"chat: card node release failed: {ex.Message}"); }
                    }
                    vp.QueueFree();
                });
            }
        }
    }

    // ---- tooltips ----

    /// <summary>
    /// The game's own tooltip (NHoverTipSet: a card shows as the card with its keywords beside it, a relic as the
    /// game's relic tip). The game puts it in its own canvas, which the chat panel covers (and masks outside the game
    /// area), so it is moved into the panel's layer and placed right of <paramref name="anchor"/> (panel units; it
    /// may reach over the game). False when the game would not show one (the text tooltip is used instead).
    /// </summary>
    private bool ShowGameTip(Control owner, float y, List<IHoverTip> tips)
    {
        HideTip();
        if (tips.Count == 0) return false;
        try
        {
            var set = NHoverTipSet.CreateAndShow(owner, tips, HoverTipAlignment.Right);
            if (set == null) return false;
            _gameTipOwner = owner;
            set.Reparent(Layer, false);
            set.Position = Vector2.Zero;
            set.Scale = Vector2.One;
            set.ZIndex = 20;
            set.MouseFilter = Control.MouseFilterEnum.Ignore;
            PlaceGameTip(set, y);
            // Containers size themselves when their content is laid out.
            Callable.From(() => { if (_gameTipOwner == owner && GodotObject.IsInstanceValid(set)) PlaceGameTip(set, y); }).CallDeferred();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: game tooltip failed: {ex.Message}");
            if (_gameTipOwner != null) NHoverTipSet.Remove(_gameTipOwner);
            _gameTipOwner = null;
            return false;
        }
    }

    private const float TipGap = 12;
    private const float ScreenMargin = 8;
    private const float LayerHeight = 1080; // the panel layer is 1080 units tall

    /// <summary>Where tooltips may go (panel units): from just right of the chat panel to the window's right edge.</summary>
    private (float Left, float Right) TipColumn()
    {
        float left = _background.Position.X + _background.Size.X + TipGap;
        float right = _lastLayout is { } l && l.Win.Y > 0 ? l.Win.X / (l.Win.Y / LayerHeight) : 1920;
        return (left, right - ScreenMargin);
    }

    private static float ClampY(float y, float height) =>
        Mathf.Clamp(y, ScreenMargin, Mathf.Max(ScreenMargin, LayerHeight - height - ScreenMargin));

    /// <summary>The card picture first, the keyword tips right of it; all moved left if it would leave the screen.</summary>
    private void PlaceGameTip(Control set, float y)
    {
        var cards = Ui.GetField(set, "_cardHoverTipContainer") as Control;
        var text = Ui.GetField(set, "_textHoverTipContainer") as Control;
        bool hasCards = cards != null && cards.GetChildCount() > 0;
        bool hasText = text != null && text.GetChildCount() > 0;
        float width = (hasCards ? cards!.Size.X : 0) + (hasCards && hasText ? 6 : 0) + (hasText ? text!.Size.X : 0);
        var (left, right) = TipColumn();
        float x = Mathf.Max(ScreenMargin, Mathf.Min(left, right - width));
        if (hasCards)
        {
            cards!.Position = new Vector2(x, ClampY(y, cards.Size.Y));
            x += cards.Size.X + 6;
        }
        if (text != null) text.Position = new Vector2(x, ClampY(y, text.Size.Y));
    }

    /// <summary>A text tooltip right of the chat panel at height <paramref name="y"/> (panel units), kept on screen.</summary>
    private void ShowTip(float y, List<(string Title, string Body)> entries)
    {
        HideTip();
        if (entries.Count == 0) return;
        _tip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 10 };
        _tip.AddThemeStyleboxOverride("panel", ChatTheme.CardStyle());
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 10);
        float width = TipWidth;
        foreach (var (title, body) in entries)
        {
            if (title.Length > 0)
            {
                var t = new Label { Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                ChatTheme.StyleText(t, 20, ChatTheme.WaitColor);
                if (ChatTheme.Bold != null) t.AddThemeFontOverride("font", ChatTheme.Bold);
                box.AddChild(t);
            }
            if (body.Length > 0)
            {
                var b = new Label { Text = body, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                ChatTheme.StyleText(b, 18);
                box.AddChild(b);
            }
        }
        _tip.AddChild(box);
        _tip.CustomMinimumSize = new Vector2(width, 0);
        Layer.AddChild(_tip);
        _tip.Size = new Vector2(width, 0);
        PlaceTip(y);
        // Wrapped labels report their height a frame late.
        var tip = _tip;
        Callable.From(() => { if (_tip == tip) PlaceTip(y); }).CallDeferred();
    }

    private void PlaceTip(float y)
    {
        if (_tip == null) return;
        _tip.Size = new Vector2(_tip.Size.X, 0);
        var (left, right) = TipColumn();
        float x = Mathf.Max(ScreenMargin, Mathf.Min(left, right - _tip.Size.X));
        _tip.Position = new Vector2(x, ClampY(y, _tip.Size.Y));
    }

    private void HideTip()
    {
        if (_tip != null && GodotObject.IsInstanceValid(_tip)) _tip.QueueFree();
        _tip = null;
        if (_gameTipOwner != null) NHoverTipSet.Remove(_gameTipOwner);
        _gameTipOwner = null;
    }
}
