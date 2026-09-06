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
                if (++ticks % 120 == 0)
                    PatchHelper.Log(
                        $"[Portrait] aspect guard: {window.ContentScaleAspect} {scale.X}x{scale.Y} mode={window.ContentScaleMode}"
                    );
                if (window.ContentScaleAspect != ExpectedAspect()
                    || (scale.X > 0 && scale.Y > 0 && scale.X > scale.Y))
                {
                    PatchHelper.Log("[Portrait] aspect drifted to landscape; re-applying");
                    Apply();
                }
                // Emulation file changed (new ratio, or removed): rebuild the
                // canvas and kick every layout through the size change.
                if (ticks % 30 == 0)
                {
                    var wanted = ReadAspectOverride();
                    if (wanted != _appliedAspect)
                    {
                        _appliedAspect = wanted;
                        PatchHelper.Log($"[Portrait] aspect emulation: {(wanted is float w ? w.ToString("F3") : "off")}");
                        Apply();
                        ForceRefresh();
                    }
                }
                // File-triggered viewport dump: drop user://sts2_vpdump to
                // capture what GODOT thinks the frame looks like, separating
                // an in-engine layout bug from a stale Android composition.
                // Dev cheat: drop user://sts2_weaken to set every living enemy
                // to 1 hp. Test runs must not grind real fights; one hit after
                // this ends the combat through the game's own death pipeline
                // (LoseHpInternal never fires death events, so the HP write is
                // safe and the kill still happens the legitimate way).
                // Dev cheat: user://sts2_lowhp puts every player creature at
                // 1 hp with no block, so the next enemy turn ends the run
                // through the game's own death pipeline (game over screen).
                var lowHpTrigger = "user://sts2_lowhp";
                if (Godot.FileAccess.FileExists(lowHpTrigger))
                {
                    DirAccess.RemoveAbsolute(lowHpTrigger);
                    try
                    {
                        var cmType = HarmonyLib.AccessTools.TypeByName(
                            "MegaCrit.Sts2.Core.Combat.CombatManager");
                        var cm = cmType?.GetProperty("Instance")?.GetValue(null);
                        var state = HarmonyLib.Traverse.Create(cm).Field("_state").GetValue();
                        var players = HarmonyLib.Traverse.Create(state).Property("PlayerCreatures").GetValue()
                            as System.Collections.IEnumerable;
                        var hurt = 0;
                        if (players is not null)
                            foreach (var creature in players)
                            {
                                var t = HarmonyLib.Traverse.Create(creature);
                                t.Property("CurrentHp").SetValue(1);
                                try { t.Property("Block").SetValue(0); } catch { }
                                hurt++;
                            }
                        PatchHelper.Log($"[Portrait] lowhp cheat: {hurt} player creature(s) at 1 hp");
                    }
                    catch (Exception e)
                    {
                        PatchHelper.Log($"[Portrait] lowhp cheat failed: {e.Message}");
                    }
                }
                var weakenTrigger = "user://sts2_weaken";
                if (Godot.FileAccess.FileExists(weakenTrigger))
                {
                    DirAccess.RemoveAbsolute(weakenTrigger);
                    try
                    {
                        var cmType = HarmonyLib.AccessTools.TypeByName(
                            "MegaCrit.Sts2.Core.Combat.CombatManager");
                        var cm = cmType?.GetProperty("Instance")?.GetValue(null);
                        var state = HarmonyLib.Traverse.Create(cm).Field("_state").GetValue();
                        var enemies = HarmonyLib.Traverse.Create(state).Property("Enemies").GetValue()
                            as System.Collections.IEnumerable;
                        var weakened = 0;
                        if (enemies is not null)
                            foreach (var enemy in enemies)
                            {
                                var t = HarmonyLib.Traverse.Create(enemy);
                                if (t.Property("IsAlive").GetValue() is true)
                                {
                                    t.Property("CurrentHp").SetValue(1);
                                    // Block would still soak a 1-hp strike.
                                    try { t.Property("Block").SetValue(0); } catch { }
                                    weakened++;
                                }
                            }
                        PatchHelper.Log($"[Cheat] weakened {weakened} enemies to 1 hp");
                    }
                    catch (Exception ex)
                    {
                        PatchHelper.Log($"[Cheat] weaken failed: {ex.GetBaseException().Message}");
                    }
                }
                // Dev probe: user://sts2_zoom holding a factor zooms the open
                // map about its center, since adb cannot fake a two-finger pinch.
                var zoomTrigger = "user://sts2_zoom";
                if (Godot.FileAccess.FileExists(zoomTrigger))
                {
                    var text = Godot.FileAccess.GetFileAsString(zoomTrigger).Trim();
                    DirAccess.RemoveAbsolute(zoomTrigger);
                    if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var factor))
                    {
                        var size = (Vector2)window.ContentScaleSize;
                        PortraitMap.BeginPinch(size * 0.5f);
                        PortraitMap.SetZoom(factor);
                        PatchHelper.Log($"[Portrait] zoom probe {factor:F2} -> {PortraitMap.Zoom:F2}");
                    }
                }
                var panTrigger = "user://sts2_pan";
                if (Godot.FileAccess.FileExists(panTrigger))
                {
                    var text = Godot.FileAccess.GetFileAsString(panTrigger).Trim();
                    DirAccess.RemoveAbsolute(panTrigger);
                    if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dx))
                    {
                        PortraitMap.PanX(dx);
                        PatchHelper.Log($"[Portrait] pan probe {dx:F0}");
                    }
                }
                var typesTrigger = "user://sts2_types";
                if (Godot.FileAccess.FileExists(typesTrigger))
                {
                    DirAccess.RemoveAbsolute(typesTrigger);
                    var sb = new System.Text.StringBuilder();
                    void Walk(Node n, int depth)
                    {
                        if (depth > 7)
                            return;
                        if (n is Control { Visible: true } c && c.IsVisibleInTree()
                            && c.Size.X > 300f && c.Size.Y > 300f)
                            sb.Append($"{c.GetType().Name}:{c.Name} | ");
                        if ((n.Name.ToString().Contains("Ping", StringComparison.OrdinalIgnoreCase)
                                || n.Name.ToString().Contains("Back", StringComparison.OrdinalIgnoreCase))
                            && n is Control pc)
                            sb.Append($"PING>{pc.GetType().Name}:{pc.Name} vis={pc.IsVisibleInTree()} rect={pc.GetGlobalRect().Position.X:F0},{pc.GetGlobalRect().Position.Y:F0} {pc.Size.X:F0}x{pc.Size.Y:F0} parent={pc.GetParent()?.Name} | ");
                        foreach (var child in n.GetChildren())
                            Walk(child, depth + 1);
                    }
                    Walk(window, 0);
                    PatchHelper.Log("[Portrait] big visibles: " + sb);
                    // Input gates: who is on top of the map, and is the map itself listening.
                    void Gates(Node n, int depth)
                    {
                        if (depth > 8)
                            return;
                        var name = n.Name.ToString();
                        if (n is Control gc && (name.EndsWith("Backstop") || name.EndsWith("ScreensContainer") || name == "OverlayScreensContainer" || name == "CapstoneScreenContainer"))
                        {
                            var kids = new System.Text.StringBuilder();
                            foreach (var k in n.GetChildren())
                                if (k is Control kc)
                                    kids.Append($"{kc.Name}(vis={kc.Visible},filter={kc.MouseFilter}) ");
                            PatchHelper.Log($"[Portrait] gate {gc.GetPath()} vis={gc.IsVisibleInTree()} filter={gc.MouseFilter} kids: {kids}");
                        }
                        if (n.GetType().Name == "NMapScreen")
                        {
                            try
                            {
                                var t = HarmonyLib.Traverse.Create(n);
                                var tween = t.Field("_actAnimTween").GetValue() as Tween;
                                PatchHelper.Log($"[Portrait] map gates: inputDisabled={t.Field("_isInputDisabled").GetValue()} actAnimRunning={(tween is not null && tween.IsValid() && tween.IsRunning())} drawing={t.Field("_drawingInput").GetValue() is not null} dragging={t.Field("_isDragging").GetValue()} target={t.Field("_targetDragPos").GetValue()} container={t.Field("_mapContainer").GetValue<Control>()?.Position} filter={(n as Control)?.MouseFilter}");
                            }
                            catch (Exception e)
                            {
                                PatchHelper.Log($"[Portrait] map gates failed: {e.Message}");
                            }
                            try
                            {
                                var t = HarmonyLib.Traverse.Create(n);
                                var drawings = t.Property("Drawings").GetValue();
                                var mode = drawings is null ? "null" : HarmonyLib.Traverse.Create(drawings).Method("GetLocalDrawingMode").GetValue()?.ToString();
                                var pts = new System.Text.StringBuilder();
                                var count = 0;
                                void Points(Node q)
                                {
                                    foreach (var k in q.GetChildren())
                                    {
                                        if (k.GetType().Name.Contains("MapPoint") && k is Control pc && count < 12)
                                        {
                                            var pt = HarmonyLib.Traverse.Create(k);
                                            var trav = pt.Property("IsTravelable").GetValue();
                                            var en = pt.Field("_isEnabled").GetValue();
                                            var r = pc.GetGlobalRect();
                                            if (trav is true || count < 4)
                                            {
                                                pts.Append($"{k.Name} trav={trav} en={en} rect={r.Position.X:F0},{r.Position.Y:F0} {r.Size.X:F0}x{r.Size.Y:F0} filter={pc.MouseFilter} | ");
                                                count++;
                                            }
                                        }
                                        Points(k);
                                    }
                                }
                                Points(n);
                                PatchHelper.Log($"[Portrait] map points: drawMode={mode} {pts}");
                            }
                            catch (Exception e)
                            {
                                PatchHelper.Log($"[Portrait] map points failed: {e.Message}");
                            }
                        }
                        foreach (var child in n.GetChildren())
                            Gates(child, depth + 1);
                    }
                    Gates(window, 0);
                }
                // sts2_tree: file body names a control (or type:<Class>); its
                // subtree is logged (name, type, rect, scale, visibility, clip,
                // text). sts2_dump belongs to the older region dump.
                var dumpTrigger = "user://sts2_tree";
                if (Godot.FileAccess.FileExists(dumpTrigger))
                {
                    var wanted = Godot.FileAccess.GetFileAsString(dumpTrigger).Trim();
                    DirAccess.RemoveAbsolute(dumpTrigger);
                    Node found = null;
                    // "type:Class:N" picks the Nth match (1-based) of that class.
                    var wantIndex = 1;
                    var byType = wanted.StartsWith("type:", StringComparison.Ordinal);
                    var wantName = byType ? wanted[5..] : wanted;
                    if (byType && wantName.Contains(':'))
                    {
                        var parts = wantName.Split(':');
                        wantName = parts[0];
                        int.TryParse(parts[1], out wantIndex);
                    }
                    var seen = 0;
                    void Find(Node n, int depth)
                    {
                        if (found is not null || depth > 14)
                            return;
                        if (n is Control && (byType ? n.GetType().Name == wantName : n.Name == wantName))
                        {
                            if (++seen >= wantIndex)
                            {
                                found = n;
                                return;
                            }
                        }
                        foreach (var child in n.GetChildren())
                            Find(child, depth + 1);
                    }
                    Find(window, 0);
                    if (found is null)
                        PatchHelper.Log($"[Portrait] dump: no control named {wanted}");
                    else
                        PortraitNodes.DumpSubtree(found, "dump", 8);
                }
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

    private static float? _appliedAspect;

    private static Window.ContentScaleAspectEnum ExpectedAspect()
        => ReadAspectOverride() is null
            ? Window.ContentScaleAspectEnum.Expand
            : Window.ContentScaleAspectEnum.Keep;

    // "W:H" as height over width; null when absent, unreadable or "off".
    private static float? ReadAspectOverride()
    {
        const string path = "user://sts2_aspect";
        try
        {
            if (!Godot.FileAccess.FileExists(path))
                return null;
            var text = Godot.FileAccess.GetFileAsString(path).Trim();
            if (text.Length == 0 || text.Equals("off", StringComparison.OrdinalIgnoreCase))
                return null;
            var parts = text.Split(':');
            if (parts.Length != 2
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
                || w <= 0f || h <= 0f)
                return null;
            // Accept either order; the phone is portrait, so the ratio is the
            // long side over the short side.
            return Math.Max(w, h) / Math.Min(w, h);
        }
        catch
        {
            return null;
        }
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
        // Phone emulation: user://sts2_aspect holding "W:H" (16:9, 18:9,
        // 19.5:9 ...) letterboxes the canvas to that shape on this device,
        // so layouts can be checked against shorter screens without owning
        // them. The file persists across boots; "off" or deleting it ends it.
        var ratio = ReadAspectOverride();
        if (ratio is float r)
            target = new Vector2I(target.X, Mathf.RoundToInt(canvasWidth * r));

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
        window.ContentScaleAspect = ExpectedAspect();
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
