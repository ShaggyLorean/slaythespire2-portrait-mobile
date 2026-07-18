using Godot;

namespace Sts2Portrait;

public static class PortraitDisplay
{
    private static bool _devWindowApplied;

    public static Window? MainWindow =>
        Engine.GetMainLoop() is SceneTree tree ? tree.Root : null;

    private static bool _orientationApplied;
    private static bool _retryHooked;
    private static bool _applying;
    private static Vector2I _lastCanvas = Vector2I.Zero;
    private static double _accum;

    /// <summary>Returns true if we own (applied) the portrait canvas — caller may skip the game's own logic.</summary>
    public static bool ApplyPortrait()
    {
        if (_applying) return true;
        _applying = true;
        try { return ApplyPortraitCore(); }
        finally { _applying = false; }
    }

    private static bool ApplyPortraitCore()
    {
        var window = MainWindow;
        if (window is null)
            return false;

        StatusStripHider.Start();

        if (!_orientationApplied && !OS.HasFeature("pc"))
        {
            _orientationApplied = true;
            try { DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait); }
            catch (System.Exception e) { PortraitMod.Log($"orientation set failed: {e.Message}"); }
            PortraitMod.Log("android orientation -> Portrait");
            if (Engine.GetMainLoop() is SceneTree t && !_retryHooked)
            {
                _retryHooked = true;
                t.ProcessFrame += RetryUntilPortrait;
            }
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

        // On Android the root viewport Size reports the content-scale size, not the OS window;
        // use the display-server window size to detect portrait reliably.
        var winSize = (Vector2)DisplayServer.WindowGetSize();
        if (!PortraitConfig.IsPortrait(winSize))
            return false;

        float aspect = winSize.Y / winSize.X;
        float scale = PortraitConfig.GetAdaptiveScale(winSize);
        int baseWidth = (int)(PortraitConfig.CanvasBaseWidth / scale);
        var target = new Vector2I(baseWidth, (int)(baseWidth * aspect));

        if (window.ContentScaleSize == target
            && window.ContentScaleMode == Window.ContentScaleModeEnum.CanvasItems
            && window.ContentScaleAspect == Window.ContentScaleAspectEnum.Expand)
            return true;

        // Each setter emits the window's changed signal, which synchronously invokes a STALE
        // (disposed) STS2Mobile launcher callback that throws — the value still applies, so we
        // swallow the throw per-set and continue, otherwise Size never gets set.
        // Each setter emits the window's changed signal, which can synchronously reach a stale
        // launcher callback that throws — the value still applies, so swallow per-set and continue.
        try { window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems; } catch { }
        try { window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand; } catch { }
        try { window.ContentScaleSize = target; } catch { }

        if (target != _lastCanvas)
        {
            _lastCanvas = target;
            PortraitMod.Log($"portrait canvas: window={winSize} scale={scale:F2} contentScaleSize={target}");
        }
        return true;
    }

    private static void RetryUntilPortrait()
    {
        _accum += 0.016;
        if (_accum < 0.3) return;
        _accum = 0;
        var w = MainWindow;
        if (w is not null && PortraitConfig.IsPortrait((Vector2)w.Size))
            ApplyPortrait();
    }
}
