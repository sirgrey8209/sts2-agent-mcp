using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

internal sealed record CaptureResult(string Path, int Width, int Height, byte[] Data, string MimeType);

/// <summary>A card to draw on a card sheet, with an optional caption (e.g. its id "c3" or option index).</summary>
internal sealed record SheetCard(CardModel Card, string? Caption, PileType Pile);

/// <summary>
/// Images for chat play: screenshots of the game window, and "card sheets" drawn with the game's own card node (NCard)
/// in an off-screen SubViewport, so any card can be shown (hand, deck, rewards, shop) in the game's real look.
/// Files are written to the capture directory so they can be forwarded to the user.
/// </summary>
internal static class Capture
{
    private const int KeepFiles = 60;

    private static string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sts2-agent-mcp");

    public static string Directory => _dir;

    public static void Configure(string? dir)
    {
        if (!string.IsNullOrWhiteSpace(dir)) _dir = dir;
    }

    /// <summary>Main thread. The last rendered frame of the game window.</summary>
    public static CaptureResult Screenshot(int maxWidth, bool wholeWindow = false)
    {
        var image = Ui.Root.GetViewport().GetTexture().GetImage();
        // With the in-game chat panel on (STS2AgentChat), show only the game (the panel is not part of what the agent
        // should see). The chat mod tells where the game is through the bridge, also to the other mod.
        if (!wholeWindow && Bridge.GameRect() is { Length: 4 } r)
        {
            var rect = new Rect2I(r[0], r[1], r[2], r[3]).Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
            if (rect.Size.X > 0 && rect.Size.Y > 0) image = image.GetRegion(rect);
        }
        Fit(image, maxWidth);
        // Full scenes compress poorly as PNG (~850 KB at 1220px); JPEG keeps them light for chat.
        return Save(image, "screen", jpeg: true);
    }

    /// <summary>Renders the cards as a grid image. Spans several frames; call from a background thread.</summary>
    public static async Task<CaptureResult> RenderCards(IReadOnlyList<SheetCard> cards, int columns, int maxWidth)
    {
        var sheet = await MainThread.Run(() => CardSheet.Create(cards, columns));
        try
        {
            // Let the cards enter the tree, lay out and render (a few frames).
            int frames = 0;
            await MainThread.Poll(() => ++frames >= 4 ? "ok" : null, TimeSpan.FromSeconds(5), () => "timeout");
            return await MainThread.Run(() =>
            {
                var image = sheet.Viewport.GetTexture().GetImage();
                Fit(image, maxWidth);
                return Save(image, "cards");
            });
        }
        finally
        {
            await MainThread.Run(sheet.Dispose);
        }
    }

    /// <summary>Main thread. Saves what an off-screen viewport rendered (PNG: flat drawings compress well).</summary>
    public static CaptureResult SaveViewport(SubViewport viewport, string prefix, int maxWidth)
    {
        var image = viewport.GetTexture().GetImage();
        Fit(image, maxWidth);
        return Save(image, prefix);
    }

    private static void Fit(Image image, int maxWidth)
    {
        if (maxWidth <= 0 || image.GetWidth() <= maxWidth) return;
        int height = (int)Math.Round(image.GetHeight() * (double)maxWidth / image.GetWidth());
        image.Resize(maxWidth, height, Image.Interpolation.Lanczos);
    }

    private static CaptureResult Save(Image image, string prefix, bool jpeg = false)
    {
        System.IO.Directory.CreateDirectory(_dir);
        string extension = jpeg ? "jpg" : "png";
        string path = System.IO.Path.Combine(_dir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{extension}");
        if (jpeg) image.Convert(Image.Format.Rgb8);
        byte[] data = jpeg ? image.SaveJpgToBuffer(0.85f) : image.SavePngToBuffer();
        File.WriteAllBytes(path, data);
        Prune();
        return new CaptureResult(path, image.GetWidth(), image.GetHeight(), data, jpeg ? "image/jpeg" : "image/png");
    }

    private static void Prune()
    {
        try
        {
            var files = new DirectoryInfo(_dir).GetFiles("*.png").Concat(new DirectoryInfo(_dir).GetFiles("*.jpg"));
            foreach (var old in files.OrderByDescending(f => f.CreationTimeUtc).Skip(KeepFiles))
                old.Delete();
        }
        catch (Exception ex)
        {
            Log.Warn($"capture prune failed: {ex.Message}");
        }
    }

    /// <summary>Off-screen grid of NCard nodes. Create and Dispose on the main thread.</summary>
    private sealed class CardSheet
    {
        private const float CardScale = 0.75f;
        private const int Padding = 16;
        private const int CaptionHeight = 40;

        private readonly List<NCard> _cards = new();

        private CardSheet(SubViewport viewport) => Viewport = viewport;

        public SubViewport Viewport { get; }

        public static CardSheet Create(IReadOnlyList<SheetCard> cards, int columns)
        {
            columns = Math.Clamp(columns, 1, Math.Max(1, cards.Count));
            int rows = (cards.Count + columns - 1) / columns;
            var cardSize = NCard.defaultSize * CardScale;
            int cellWidth = (int)cardSize.X + Padding;
            int cellHeight = (int)cardSize.Y + Padding + CaptionHeight;

            var viewport = new SubViewport
            {
                Size = new Vector2I(columns * cellWidth + Padding, rows * cellHeight + Padding),
                TransparentBg = false,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            Ui.Root.AddChild(viewport);
            viewport.AddChild(new ColorRect { Color = new Color(0.09f, 0.09f, 0.11f), Size = viewport.Size });

            var sheet = new CardSheet(viewport);
            for (int i = 0; i < cards.Count; i++)
            {
                int col = i % columns, row = i / columns;
                var cellOrigin = new Vector2(Padding + col * cellWidth, Padding + row * cellHeight);
                sheet.AddCard(cards[i], cellOrigin, cardSize);
            }
            return sheet;
        }

        private void AddCard(SheetCard entry, Vector2 cellOrigin, Vector2 cardSize)
        {
            var model = entry.Card.IsMutable ? entry.Card : entry.Card.ToMutable();
            var node = NCard.Create(model);
            if (node == null) return;
            _cards.Add(node);
            Viewport.AddChild(node);
            node.Visible = true;
            node.Modulate = Colors.White;
            node.Rotation = 0;
            node.Scale = Vector2.One * CardScale;
            // NCard draws centered on its position.
            node.Position = cellOrigin + cardSize / 2;
            try { node.UpdateVisuals(entry.Pile, CardPreviewMode.Normal); }
            catch (Exception ex) { Log.Warn($"card visuals failed for {model.Id}: {ex.Message}"); }

            if (!string.IsNullOrEmpty(entry.Caption))
            {
                Viewport.AddChild(new Label
                {
                    Text = entry.Caption,
                    Position = new Vector2(cellOrigin.X, cellOrigin.Y + cardSize.Y + 4),
                    Size = new Vector2(cardSize.X, CaptionHeight - 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    LabelSettings = new LabelSettings { FontSize = 26, OutlineSize = 6, OutlineColor = Colors.Black },
                });
            }
        }

        public void Dispose()
        {
            foreach (var card in _cards)
            {
                try
                {
                    Viewport.RemoveChild(card);
                    NodePool.Free(card);
                }
                catch (Exception ex)
                {
                    Log.Warn($"card node release failed: {ex.Message}");
                }
            }
            _cards.Clear();
            Viewport.QueueFree();
        }
    }
}
