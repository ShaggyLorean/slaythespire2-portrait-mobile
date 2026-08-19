using System;
using Godot;

namespace STS2Mobile.Portrait;

// The game ships with no frame cap and vsync follows the panel, so on a
// 120 Hz phone a turn-based card game renders 4.5M pixels 120 times a second
// and the device runs hot. A 60 fps budget halves GPU and CPU load with no
// gameplay cost; animations stay smooth on the panel's even divisor.
//
// user://sts2_fps_cap overrides the default (an integer; 0 disables the cap
// entirely), which is also how the before/after heat measurements were taken.
internal static class PortraitFrameBudget
{
    private const int DefaultCap = 60;
    private static int _resolvedCap = DefaultCap;
    private static bool _monitorStarted;

    internal static void ApplyEarly()
    {
        try
        {
            if (!OperatingSystem.IsAndroid())
                return;

            _resolvedCap = ReadConfiguredCap();
            if (_resolvedCap > 0)
                Engine.MaxFps = _resolvedCap;

            BootstrapTrace.Log(
                $"Frame budget: cap={_resolvedCap} MaxFps={Engine.MaxFps} panel={DisplayServer.ScreenGetRefreshRate():F0}Hz"
            );
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Frame budget failed: {ex.GetBaseException().Message}");
        }
    }

    // The game's own settings pass can rewrite Engine.MaxFps after boot, so a
    // slow heartbeat re-asserts the budget whenever it finds the cap dropped
    // back to uncapped. Values the game sets on purpose (the 30 fps background
    // limit) are left alone. The same tick logs the measured fps, which is the
    // evidence trail for the thermal work.
    internal static void StartMonitor(SceneTree tree)
    {
        if (_monitorStarted || !OperatingSystem.IsAndroid() || tree is null)
            return;

        _monitorStarted = true;
        Tick(tree);
    }

    private static void Tick(SceneTree tree)
    {
        tree.CreateTimer(20.0).Timeout += () =>
        {
            try
            {
                if (_resolvedCap > 0 && Engine.MaxFps == 0)
                {
                    Engine.MaxFps = _resolvedCap;
                    PatchHelper.Log($"[FrameBudget] cap re-asserted at {_resolvedCap}");
                }

                PatchHelper.Log(
                    $"[FrameBudget] fps={Performance.GetMonitor(Performance.Monitor.TimeFps):F0}"
                    + $" cpu={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:F1}ms"
                    + $" objs={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):F0}"
                    + $" draw={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0}"
                    + $" prim={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):F0}"
                    + $" vram={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0):F0}MB"
                    + $" maxFps={Engine.MaxFps}"
                );
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[FrameBudget] tick failed: {ex.GetBaseException().Message}");
            }

            Tick(tree);
        };
    }

    private static int ReadConfiguredCap()
    {
        try
        {
            var capFile = System.IO.Path.Combine(OS.GetUserDataDir(), "sts2_fps_cap");
            if (System.IO.File.Exists(capFile)
                && int.TryParse(System.IO.File.ReadAllText(capFile).Trim(), out var configured)
                && configured >= 0)
                return configured;
        }
        catch
        {
            // Unreadable override means the default budget applies.
        }

        return DefaultCap;
    }
}
