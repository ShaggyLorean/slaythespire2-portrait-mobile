using Godot;

namespace Sts2Portrait;

public static class PortraitConfig
{
    public const int CanvasBaseWidth = 1080;

    public static readonly Vector2I DevWindowSize = new(720, 1440);

    public static bool IsPortrait(Vector2 windowSize) => windowSize.Y > windowSize.X;

    public static float GetAdaptiveScale(Vector2 windowSize)
    {
        if (windowSize.X <= 0) return 1f;
        float aspect = windowSize.Y / windowSize.X;
        return aspect > 2.0f ? 0.92f : 1.0f;
    }

    public static float SafeAreaTop = 40f;

    public static float SafeAreaBottom = 30f;

    public static Vector2 CanvasSize =>
        Engine.GetMainLoop() is SceneTree t ? (Vector2)t.Root.ContentScaleSize : new Vector2(1173, 2541);

    public static Vector2 CanvasCenter => CanvasSize * 0.5f;
}
