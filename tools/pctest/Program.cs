using System;
using STS2Mobile.Portrait;

namespace STS2Mobile.PcTest;

// Pure-math regression checks for the layout values the device tests depend
// on. Runs on plain .NET; no Godot runtime is initialized.
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        // Compact bar geometry at zero inset (cutout-less device).
        AssertEqual(8f, PortraitHudMetrics.HudTop(0f), "HudTop at 0 inset");
        AssertEqual(8f + 86f * 2f, PortraitHudMetrics.HudBottom(0f), "HudBottom at 0 inset");
        AssertEqual(PortraitHudMetrics.HudBottom(0f) + 16f, PortraitHudMetrics.ContentTop(0f), "ContentTop at 0 inset");

        // Combat stack geometry stays the v0.3.0 tuning.
        AssertEqual(24f, PortraitHudMetrics.CombatHudTop(0f), "CombatHudTop at 0 inset");
        AssertEqual(24f + 394f + 104f, PortraitHudMetrics.CombatHudBottom(0f), "CombatHudBottom at 0 inset");

        // The band tracks the safe-area inset one to one.
        AssertEqual(
            PortraitHudMetrics.ContentTop(0f) + 127f,
            PortraitHudMetrics.ContentTop(127f),
            "ContentTop tracks safeTop linearly"
        );

        // Combat top band: art floor wins on cutout-less devices, cutout wins
        // once the inset reaches past the art strip.
        AssertEqual(118f, PortraitHudMetrics.CombatTopBandHeight(0f), "combat band floor, no cutout");
        AssertEqual(118f, PortraitHudMetrics.CombatTopBandHeight(100f), "combat band floor, shallow cutout");
        AssertEqual(206f, PortraitHudMetrics.CombatTopBandHeight(200f), "combat band grows with deep cutout");

        // Row order stays strictly increasing so the HUD stack cannot overlap
        // itself if someone edits one constant.
        AssertIncreasing(
            "HUD row offsets",
            0f,
            PortraitHudMetrics.GoldRowOffset,
            PortraitHudMetrics.PotionRowOffset,
            PortraitHudMetrics.RoomRowOffset,
            PortraitHudMetrics.RelicRowOffset
        );

        // Sanity: on a tall reference-like canvas, content now starts in the
        // top ~15% of the screen (slim bar), not buried below a tall stack.
        var referenceInsetCanvasUnits = 127f;
        var referenceCanvasHeight = 2361f;
        var contentTop = PortraitHudMetrics.ContentTop(referenceInsetCanvasUnits);
        Assert(
            contentTop < referenceCanvasHeight * 0.15f,
            $"ContentTop {contentTop:F0} stays above 15% of the reference canvas"
        );

        Console.WriteLine(
            _failures == 0
                ? "OK: PortraitHudMetrics checks passed"
                : $"FAILED: {_failures} PortraitHudMetrics check(s)"
        );
        return _failures == 0 ? 0 : 1;
    }

    private static void AssertEqual(float expected, float actual, string label)
        => Assert(Math.Abs(expected - actual) < 0.01f, $"{label}: expected {expected}, got {actual}");

    private static void AssertIncreasing(string label, params float[] values)
    {
        for (var i = 1; i < values.Length; i++)
            Assert(values[i] > values[i - 1], $"{label}: {values[i]} must be > {values[i - 1]}");
    }

    private static void Assert(bool condition, string message)
    {
        if (condition)
            return;
        _failures++;
        Console.WriteLine($"FAIL: {message}");
    }
}
