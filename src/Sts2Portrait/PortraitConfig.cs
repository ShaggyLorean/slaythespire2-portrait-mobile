using Godot;

namespace Sts2Portrait;

public static class PortraitConfig
{
    public const int CanvasBaseWidth = 1080;

    public static readonly Vector2I DevWindowSize = new(655, 1440);   // phone-like 19.8:9

    // Dynamic canvas sizing. The content-scale canvas width sets how big every UI element
    // is on screen: the physical screen maps onto this many canvas units, so fewer units =
    // bigger UI. We express it as a magnification of the window (DPI-independent, so every
    // phone gets the same relative UI size regardless of resolution). Below a floor the game
    // can't fill the screen, so it's clamped.
    public static float UiMagnify = 1.35f;     // UI is ~this much bigger than a 1:1 canvas
    public static float MinCanvasWidth = 1066f;   // matches the OnePlus 13 canvas so PC debug is 1:1
    public static float MaxCanvasWidth = 1280f;

    public static bool IsPortrait(Vector2 windowSize) => windowSize.Y > windowSize.X;

    public static int CanvasWidthFor(Vector2 windowSize)
    {
        if (windowSize.X <= 0) return CanvasBaseWidth;
        float w = windowSize.X / Mathf.Max(0.5f, UiMagnify);
        return (int)Mathf.Clamp(w, MinCanvasWidth, MaxCanvasWidth);
    }

    // Extra top padding added on top of the measured cutout, in canvas units.
    public static float SafeAreaTopPad = 12f;
    public static float SafeAreaBottomPad = 10f;

    public static Vector2 CanvasSize =>
        Engine.GetMainLoop() is SceneTree t ? (Vector2)t.Root.ContentScaleSize : new Vector2(1080, 2160);

    public static Vector2 CanvasCenter => CanvasSize * 0.5f;

    // Top inset (canvas units) that must stay clear of a notch / punch-hole camera.
    // Uses the OS-reported safe area vs the full window, converted into canvas units.
    public static float SafeTop()
    {
        try
        {
            var win = (Vector2)DisplayServer.WindowGetSize();
            if (win.X <= 0 || !IsPortrait(win)) return SafeAreaTopPad;
            var safe = DisplayServer.GetDisplaySafeArea();   // pixels
            float insetPx = safe.Position.Y;
            float canvasPerPx = CanvasSize.X / win.X;
            return insetPx * canvasPerPx + SafeAreaTopPad;
        }
        catch { return SafeAreaTopPad; }
    }
}
