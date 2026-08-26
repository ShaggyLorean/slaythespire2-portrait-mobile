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

    private static bool? _canvasResolution;

    private static bool RenderAtCanvasResolution()
    {
        if (_canvasResolution is { } cached)
            return cached;

        // Default is the panel's native resolution: the user's rule is that
        // the original experience loses nothing, and the viewport downscale
        // is a (mild) visual trade. The file opts INTO viewport for thermal
        // experiments instead of out of it.
        var mode = "native";
        try
        {
            var path = System.IO.Path.Combine(OS.GetUserDataDir(), "sts2_render_mode");
            if (System.IO.File.Exists(path))
                mode = System.IO.File.ReadAllText(path).Trim().ToLowerInvariant();
        }
        catch
        {
            // Unreadable override keeps the default.
        }

        _canvasResolution = OperatingSystem.IsAndroid() && mode == "viewport";
        PatchHelper.Log($"[Portrait] Render mode: {(_canvasResolution.Value ? "viewport (canvas-res)" : "native")}");
        return _canvasResolution.Value;
    }

    private static bool _aspectGuardArmed;
    private static bool _orientationRequested;

    // Save-and-Quit re-applies the game's display settings DEFERRED, landing
    // after the ApplyDisplaySettings postfix, and a content-scale change does
    // not fire OnWindowChange, so the menu came back letterboxed into a
    // landscape strip with no hook left to catch it. A slow heartbeat is the
    // only reliable owner of the aspect: re-assert whenever the window's
    // content scale stops looking like our portrait canvas.
    internal static void StartAspectGuard(SceneTree tree)
    {
        if (_aspectGuardArmed || tree?.Root is null)
            return;
        _aspectGuardArmed = true;
        var ticks = 0;
        void Tick()
        {
            try
            {
                var window = tree.Root;
                var scale = window.ContentScaleSize;
                if (++ticks % 10 == 0)
                    PatchHelper.Log(
                        $"[Portrait] aspect guard: {window.ContentScaleAspect} {scale.X}x{scale.Y} mode={window.ContentScaleMode}"
                    );
                if (window.ContentScaleAspect != Window.ContentScaleAspectEnum.Expand
                    || (scale.X > 0 && scale.Y > 0 && scale.X > scale.Y))
                {
                    PatchHelper.Log("[Portrait] aspect drifted to landscape; re-applying");
                    Apply();
                }
                // File-triggered viewport dump: drop user://sts2_vpdump to
                // capture what GODOT thinks the frame looks like, separating
                // an in-engine layout bug from a stale Android composition.
                var trigger = "user://sts2_vpdump";
                if (Godot.FileAccess.FileExists(trigger))
                {
                    DirAccess.RemoveAbsolute(trigger);
                    var img = window.GetTexture().GetImage();
                    img.SavePng("user://sts2_vpdump.png");
                    PatchHelper.Log($"[Portrait] vpdump saved {img.GetWidth()}x{img.GetHeight()}");
                }
            }
            catch
            {
                // A mid-teardown window is fine; the next tick sees the new one.
            }
            tree.CreateTimer(1.0).Timeout += Tick;
        }
        Tick();
    }

    // The engine-side setter skips the native update when the value matches
    // the C# cache, and after Save-and-Quit the NATIVE content scale drifts
    // landscape while the cache still holds our values: every same-value
    // rewrite was a no-op and the menu stayed a frozen strip. Writing a
    // deliberately different size first forces the native path, then Apply
    // lands the real target.
    internal static void ForceRefresh()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        try
        {
            var window = tree.Root;
            var current = window.ContentScaleSize;
            window.ContentScaleSize = new Vector2I(current.X, current.Y + 1);
        }
        catch
        {
            // Window mid-teardown; Apply below will be a no-op too.
        }
        Apply();
    }

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

        // canvas_items renders at the panel's native pixel count; viewport
        // mode renders at the canvas size and upscales, cutting GPU work by
        // a third on this device. The phone runs hot and GPU-bound at native,
        // so viewport is the Android default; user://sts2_render_mode with
        // the word "native" switches back for comparison.
        // Write UNCONDITIONALLY every time: an attempted only-on-change
        // optimization here broke even the boot path, because the C# window
        // properties can read back "already correct" while the native side
        // renders the landscape fit; the blind rewrite is load-bearing.
        window.ContentScaleMode = RenderAtCanvasResolution()
            ? Window.ContentScaleModeEnum.Viewport
            : Window.ContentScaleModeEnum.CanvasItems;
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

            float insetPixels;
            if (OperatingSystem.IsAndroid())
            {
                var safe = DisplayServer.GetDisplaySafeArea();
                insetPixels = top
                    ? safe.Position.Y
                    : Math.Max(0f, physicalSize.Y - safe.End.Y);
                if (top)
                {
                    insetPixels = Math.Max(
                        insetPixels,
                        (float)AndroidGodotAppBridge.GetDisplayCutoutTopInsetPixels()
                    );
                }
            }
            else
            {
                // Desktop "safe area" is the monitor's usable rect; against a
                // phone-shaped window that is larger than the monitor (the PC
                // rig) it reports a huge phantom bottom inset. Desktop windows
                // have no cutouts, so the simulated inset is the only input.
                insetPixels = SimulatedInsetPixels(top);
            }
            return insetPixels * CanvasSize.X / physicalSize.X + SafeAreaPadding;
        }
        catch
        {
            return SafeAreaPadding;
        }
    }

    // Desktop pre-screening only: the project plan requires PC debugging to
    // simulate the punch-hole/safe-area, which desktop windows do not have.
    // Values are physical pixels of the test window, matching how Android
    // reports its cutout inset. Never read on Android.
    private static float SimulatedInsetPixels(bool top)
    {
        var value = System.Environment.GetEnvironmentVariable(
            top ? "STS2_PORTRAIT_FAKE_TOP_INSET" : "STS2_PORTRAIT_FAKE_BOTTOM_INSET"
        );
        return float.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var pixels
        )
            ? Math.Max(0f, pixels)
            : 0f;
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
