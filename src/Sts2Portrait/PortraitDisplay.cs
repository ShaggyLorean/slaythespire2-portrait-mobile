using Godot;

namespace Sts2Portrait;

public static class PortraitDisplay
{
    private static bool _devWindowApplied;

    public static Window? MainWindow =>
        Engine.GetMainLoop() is SceneTree tree ? tree.Root : null;

    private static bool _orientationApplied;

    public static void ApplyPortrait()
    {
        var window = MainWindow;
        if (window is null)
            return;

        StatusStripHider.Start();

        if (!_orientationApplied && !OS.HasFeature("pc"))
        {
            _orientationApplied = true;
            try { DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait); }
            catch (System.Exception e) { PortraitMod.Log($"orientation set failed: {e.Message}"); }
            PortraitMod.Log("android orientation -> Portrait");
        }

        if (!_devWindowApplied && OS.HasFeature("pc"))
        {
            _devWindowApplied = true;
            window.Mode = Window.ModeEnum.Windowed;
            window.Size = PortraitConfig.DevWindowSize;
            var screen = DisplayServer.ScreenGetSize();
            window.Position = (screen - PortraitConfig.DevWindowSize) / 2;
            PortraitMod.Log($"dev window -> windowed {PortraitConfig.DevWindowSize}");
        }

        var winSize = (Vector2)window.Size;
        if (!PortraitConfig.IsPortrait(winSize))
        {
            PortraitMod.Log($"window not portrait ({winSize}); skipping canvas override");
            return;
        }

        float aspect = winSize.Y / winSize.X;
        float scale = PortraitConfig.GetAdaptiveScale(winSize);
        int baseWidth = (int)(PortraitConfig.CanvasBaseWidth / scale);

        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleSize = new Vector2I(baseWidth, (int)(baseWidth * aspect));

        PortraitMod.Log(
            $"portrait canvas: window={winSize} aspect={aspect:F3} scale={scale:F2} " +
            $"contentScaleSize={window.ContentScaleSize}");
    }
}
