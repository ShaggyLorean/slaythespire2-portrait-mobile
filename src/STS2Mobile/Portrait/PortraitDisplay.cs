using System;
using Godot;
using STS2Mobile.Patches;

namespace STS2Mobile.Portrait;

// Owns the portrait viewport. The desktop game normally keeps a landscape design
// canvas and expands its sides; on a phone that makes every control tiny. We keep
// the physical aspect ratio but reduce the virtual short edge so touch targets and
// cards remain readable across phones with different resolutions and cutouts.
internal static class PortraitDisplay
{
    private const float DefaultCanvasWidth = 1080f;
    private const float MaximumCanvasWidth = 1180f;
    private const float MinimumCanvasWidth = 980f;
    private const float UiMagnification = 1.10f;
    private const float SafeAreaPadding = 12f;
    private const string GuardName = "Sts2PortraitViewportGuard";
    private const string LegacyFrameName = "Sts2PortraitFrame";

    private static Vector2I _lastCanvas;

    internal static Vector2 CanvasSize
        => Engine.GetMainLoop() is SceneTree tree
            ? (Vector2)tree.Root.ContentScaleSize
            : new Vector2(DefaultCanvasWidth, DefaultCanvasWidth * 2f);

    internal static bool IsPortrait(Vector2 size) => size.Y > size.X;

    internal static bool Apply()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return false;

        var window = tree.Root;
        if (OperatingSystem.IsAndroid())
        {
            try
            {
                DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.SensorPortrait);
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[Portrait] Could not request portrait orientation: {ex.Message}");
            }
        }

        var physicalSize = (Vector2)DisplayServer.WindowGetSize();
        if (physicalSize.X <= 0 || physicalSize.Y <= 0)
            physicalSize = window.Size;
        if (!IsPortrait(physicalSize))
            return false;

        var canvasWidth = Mathf.Clamp(
            physicalSize.X / UiMagnification,
            MinimumCanvasWidth,
            MaximumCanvasWidth
        );
        var target = new Vector2I(
            Mathf.RoundToInt(canvasWidth),
            Mathf.RoundToInt(canvasWidth * physicalSize.Y / physicalSize.X)
        );

        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleSize = target;

        if (_lastCanvas != target)
        {
            _lastCanvas = target;
            PatchHelper.Log(
                $"[Portrait] Canvas {target.X}x{target.Y} for window {physicalSize.X:F0}x{physicalSize.Y:F0}"
            );
        }

        RemoveLegacyFrame(window);
        EnsureGuard(window);
        return true;
    }

    internal static float SafeTop() => SafeInset(top: true);

    internal static float SafeBottom() => SafeInset(top: false);

    private static float SafeInset(bool top)
    {
        try
        {
            var physicalSize = (Vector2)DisplayServer.WindowGetSize();
            if (physicalSize.X <= 0 || !IsPortrait(physicalSize))
                return SafeAreaPadding;

            var safe = DisplayServer.GetDisplaySafeArea();
            var insetPixels = top
                ? safe.Position.Y
                : Math.Max(0f, physicalSize.Y - safe.End.Y);
            if (top && OperatingSystem.IsAndroid())
            {
                insetPixels = Math.Max(
                    insetPixels,
                    (float)AndroidGodotAppBridge.GetDisplayCutoutTopInsetPixels()
                );
            }
            return insetPixels * CanvasSize.X / physicalSize.X + SafeAreaPadding;
        }
        catch
        {
            return SafeAreaPadding;
        }
    }

    private static void RemoveLegacyFrame(Window window)
    {
        var frame = window.GetNodeOrNull<CanvasLayer>(LegacyFrameName);
        if (frame is not null)
            frame.QueueFree();
    }

    private static void EnsureGuard(Window window)
    {
        if (window.GetNodeOrNull<Node>(GuardName) is not null)
            return;

        window.AddChild(new PortraitViewportGuard { Name = GuardName });
    }

    // Game settings and legacy mobile scale patches can both rewrite the root
    // Window after the downloaded PCK starts. Keep the real portrait canvas as
    // the single source of truth instead of allowing a later 16:9 override to
    // letterbox the entire game into a small horizontal strip.
    private sealed partial class PortraitViewportGuard : Node
    {
        private const double CheckIntervalSeconds = 0.2;
        private double _elapsed;

        public override void _Process(double delta)
        {
            _elapsed += delta;
            if (_elapsed < CheckIntervalSeconds)
                return;

            _elapsed = 0;
            Apply();
        }
    }
}
