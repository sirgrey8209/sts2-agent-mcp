using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Sts2AgentMcp.Screens;

namespace Sts2AgentMcp.Game;

/// <summary>
/// A small live picture of a creature in this combat, for places that name a target (the chat's drawer). The game has
/// no portrait images of monsters, so this looks at the creature where it stands: a viewport sharing the game's 2D
/// world draws the square around the creature's hitbox, every frame (it moves, its intent changes). The game's canvas
/// layers (the chat, the marks, the top bar) are not part of the world, so nothing laid over the creature shows. When
/// the creature leaves the screen (dies) the last picture stays. Main thread only.
/// </summary>
internal static class CreaturePortrait
{
    /// <summary>The creature behind a target id (e1, me, pet1) in this combat, or null.</summary>
    public static Creature? Resolve(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var ctx = GameCtx.Capture();
        return ctx.Combat == null ? null : Ids.ResolveCreature(ctx.Combat, ctx.Me?.Creature, target.Trim().ToLowerInvariant());
    }

    /// <summary>The side of the square that holds the largest creature on screen (its hitbox, with room for its intent).</summary>
    private static float LargestSide()
    {
        float largest = 0;
        var ctx = GameCtx.Capture();
        if (ctx.Combat == null || NCombatRoom.Instance is not { } room) return 0;
        foreach (var creature in ctx.Combat.Enemies)
        {
            if (room.GetCreatureNode(creature) is { } node && Ui.IsLive(node) && node.Hitbox is { } hitbox)
            {
                var size = hitbox.GetGlobalRect().Size;
                largest = Mathf.Max(largest, Mathf.Max(size.X, size.Y * 1.25f));
            }
        }
        return largest * 1.1f;
    }

    /// <param name="size">The picture's size where it is shown (it renders at twice that for sharpness).</param>
    public static Control Create(Creature creature, float size)
    {
        int pixels = Mathf.Max(16, (int)Mathf.Round(size * 2));
        var viewport = new SubViewport
        {
            Size = new Vector2I(pixels, pixels),
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            World2D = ((SceneTree)Engine.GetMainLoop()).Root.World2D,
        };
        var picture = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        picture.AddChild(viewport);
        picture.Texture = viewport.GetTexture();

        float side = 0;
        void Follow()
        {
            if (!GodotObject.IsInstanceValid(viewport) || !viewport.IsInsideTree()) return;
            var node = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (node == null || !Ui.IsLive(node) || node.Hitbox is not { } hitbox || !Ui.IsLive(hitbox))
            {
                // Gone (dead or the room changed): keep the last picture.
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                return;
            }
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            var rect = hitbox.GetGlobalRect();
            // One camera for every portrait: a square fitted to the largest creature in the combat (so a small one
            // looks small), standing on the square's floor, centered on the creature.
            side = Mathf.Max(side, LargestSide());
            var topLeft = new Vector2(rect.GetCenter().X - side / 2, rect.End.Y + side * 0.06f - side);
            float scale = pixels / side;
            viewport.CanvasTransform = new Transform2D(0, new Vector2(scale, scale), 0, -topLeft * scale);
        }
        var tree = (SceneTree)Engine.GetMainLoop();
        Action tick = Follow;
        tree.ProcessFrame += tick;
        picture.TreeExiting += () => tree.ProcessFrame -= tick;
        Follow();
        return picture;
    }
}
