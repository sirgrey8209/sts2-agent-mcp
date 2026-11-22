using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>
/// When the game is drawn through an extra root canvas transform (in-game chat layout), Godot routes GUI input through
/// that transform, but <see cref="Viewport.GetMousePosition"/> on the root still returns untransformed viewport
/// coordinates. Game code uses it for the dragged card and the targeting arrow, so they would drift away from the cursor.
/// This postfix maps the root viewport's mouse position into the game's canvas coordinates while a remap is active.
/// Map drawing (the quill) reads the mouse event's GlobalPosition in its own <c>_Input</c> and maps it with the drawing
/// node's global transform, which leaves out the canvas transform too: its two mouse inputs get a copy of the event
/// with the position mapped back, so the line starts under the cursor.
/// </summary>
internal static class MouseRemap
{
    private static bool _installed;
    private static ulong _rootId;
    private static Transform2D _inverse = Transform2D.Identity;
    private static bool _active;

    public static bool Installed => _installed;

    /// <summary>Main thread. Patches once; returns false if the patch could not be applied.</summary>
    public static bool Install()
    {
        if (_installed) return true;
        Features.Try("mouse-remap", () =>
        {
            var harmony = new Harmony("sirgrey8209.STS2AgentChat");
            var original = AccessTools.Method(typeof(Viewport), nameof(Viewport.GetMousePosition));
            harmony.Patch(original, postfix: new HarmonyMethod(typeof(MouseRemap), nameof(Postfix)));
            _installed = true;
        });
        Features.Try("map-drawing-remap", () =>
        {
            var harmony = new Harmony("sirgrey8209.STS2AgentChat.MapDrawing");
            var prefix = new HarmonyMethod(typeof(MouseRemap), nameof(DrawingInputPrefix));
            foreach (var type in new[] { typeof(NMouseModeMapDrawingInput), typeof(NMouseHeldMapDrawingInput) })
                harmony.Patch(AccessTools.Method(type, "_Input") ?? throw new System.MissingMethodException($"{type.Name}._Input"), prefix: prefix);
        });
        return _installed;
    }

    /// <summary>
    /// Main thread. canvasTransform: the transform applied to the game's canvas (content units).
    /// bounds: the game's own screen rect; the position is clamped to it, as the window edge clamps the real cursor
    /// (otherwise a dragged card follows the cursor into the chat panel).
    /// </summary>
    public static void Activate(Window root, Transform2D canvasTransform, Rect2 bounds)
    {
        _rootId = root.GetInstanceId();
        _inverse = canvasTransform.AffineInverse();
        _bounds = bounds;
        _active = true;
    }

    public static void Deactivate() => _active = false;

    private static Rect2 _bounds;

    /// <summary>The drawing input's mouse events, with the position in the game's canvas (a copy; others get the original).</summary>
    private static void DrawingInputPrefix(ref InputEvent __0)
    {
        if (!_active || __0 is not InputEventMouse mouse) return;
        var copy = (InputEventMouse)mouse.Duplicate();
        copy.GlobalPosition = _inverse * mouse.GlobalPosition;
        copy.Position = _inverse * mouse.Position;
        __0 = copy;
    }

    private static void Postfix(Viewport __instance, ref Vector2 __result)
    {
        if (!_active || __instance.GetInstanceId() != _rootId) return;
        var p = _inverse * __result;
        __result = new Vector2(
            Mathf.Clamp(p.X, _bounds.Position.X, _bounds.End.X),
            Mathf.Clamp(p.Y, _bounds.Position.Y, _bounds.End.Y));
    }
}
