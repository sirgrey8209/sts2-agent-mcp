using System;
using Godot;
using Sts2AgentMcp.Chat;

namespace Sts2AgentMcp.Game;

/// <summary>
/// A small label the chat agent puts on the game screen to name something ("A", "B", "1"...): a disc in the ink color
/// of what it names (the route or mark of the same color), a white letter in the game's bold font, a slightly uneven
/// rim and tilt so it sits with the hand-drawn ink. Built from plain nodes (Polygon2D, Line2D, Label); the caller places
/// it (its origin is the disc's center). Main thread only.
/// </summary>
internal static class InkBadge
{
    public const float Radius = 19f;
    private const int FontSize = 24;

    public static Node2D Create(string text, Color color, int seed)
    {
        var rng = new Random(seed);
        var root = new Node2D { Name = "AgentBadge", Rotation = (float)(rng.NextDouble() - 0.5) * 0.2f };

        // An uneven disc: the radius wanders a little around the circle.
        const int sides = 28;
        var outline = new Vector2[sides];
        float phase = (float)(rng.NextDouble() * Math.PI * 2);
        for (int i = 0; i < sides; i++)
        {
            float angle = MathF.PI * 2 * i / sides;
            float r = Radius * (1f + 0.05f * MathF.Sin(angle * 3 + phase) + 0.02f * (float)(rng.NextDouble() * 2 - 1));
            outline[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
        }
        root.AddChild(new Polygon2D { Polygon = outline, Color = color });
        var rim = new Line2D { Points = outline, Closed = true, Width = 2.5f, DefaultColor = color.Darkened(0.4f), JointMode = Line2D.LineJointMode.Round };
        root.AddChild(rim);

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(-Radius * 2, -Radius * 2),
            Size = new Vector2(Radius * 4, Radius * 4),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (ChatTheme.Bold is { } font) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", text.Length > 1 ? FontSize - 6 : FontSize);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", color.Darkened(0.55f));
        label.AddThemeConstantOverride("outline_size", 6);
        root.AddChild(label);
        return root;
    }
}
