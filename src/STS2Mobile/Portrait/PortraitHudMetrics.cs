using System;

namespace STS2Mobile.Portrait;

// Single source of truth for the vertical band the portrait run HUD occupies.
// Outside combat the HUD is a compact two-row bar over a shared backdrop band;
// in combat it expands to the full stacked layout. Content patches for
// non-combat screens use ContentTop, which is derived from the COMPACT bar, so
// screens start right below the slim bar instead of under a tall stack
// (BUG-003). Keep this class free of game and Godot types so the PC harness
// can compile and unit-test the math.
internal static class PortraitHudMetrics
{
    // Compact (non-combat) bar: two slim rows on one backdrop band.
    internal const float CompactTopPadding = 8f;
    internal const float CompactRowPitch = 86f;
    internal const float CompactHudHeight = CompactTopPadding + CompactRowPitch * 2f;

    // Breathing room between the bar and the first content line.
    internal const float ContentMargin = 16f;

    // Combat keeps the expanded stack tuned in v0.3.0.
    internal const float CombatTopPadding = 24f;
    internal const float GoldRowOffset = 92f;
    internal const float PotionRowOffset = 184f;
    internal const float RoomRowOffset = 286f;
    internal const float RelicRowOffset = 394f;
    internal const float RelicRowHeight = 104f;

    // Combat top band: must always cover the authored blue-grey sky strip of
    // the landscape background art (fixed art height), and grow to back the
    // display cutout when its inset reaches deeper than the art strip.
    internal const float CombatArtStripCover = 118f;
    internal const float CombatTopBandExtra = 6f;

    internal static float HudTop(float safeTop) => safeTop + CompactTopPadding;

    // Bottom edge of the compact bar's backdrop band, in canvas units.
    internal static float HudBottom(float safeTop) => safeTop + CompactHudHeight;

    // First Y where non-combat screen content may start.
    internal static float ContentTop(float safeTop) => HudBottom(safeTop) + ContentMargin;

    internal static float CombatHudTop(float safeTop) => safeTop + CombatTopPadding;

    internal static float CombatHudBottom(float safeTop)
        => CombatHudTop(safeTop) + RelicRowOffset + RelicRowHeight;

    internal static float CombatTopBandHeight(float safeTop)
        => Math.Max(CombatArtStripCover, safeTop + CombatTopBandExtra);

    // ---- Touch layout rules (phone-first sizing) ----
    // Every screen treats the canvas as a touch surface: controls grow to
    // use the free band, nothing renders into the gesture strip at the
    // bottom or the cutout at the top, and hit targets never fall under
    // the minimum touch side. Content patches must size through these
    // helpers instead of per-screen constants.

    // Extra clearance kept above the OS gesture area, below the safe inset.
    internal const float NavClearance = 24f;

    // Minimum acceptable side of a touch target, in canvas units.
    //
    // The old value of 96 was documented as ~10mm and was not: on the
    // reference device the 1180-unit canvas maps to a 1440px panel at 640dpi,
    // so one canvas unit is 1.22px = 0.305dp, and 96 units is 29dp - a bit
    // over 4mm, well under half of Android's 48dp minimum. That single wrong
    // constant is why so many screens measured "fine" while being visibly too
    // small to hit: every check in this layer trusted it.
    //
    // 160 units = 48.8dp = ~7.6mm, which is the platform minimum rather than
    // an ambition. Primary rows should still be given more than this.
    internal const float MinTouchSide = 160f;

    // Side margin content keeps from the canvas edges.
    internal const float EdgeMargin = 30f;

    // Last Y non-combat content may reach (start of the gesture strip).
    internal static float ContentBottom(float canvasY, float safeBottom)
        => canvasY - safeBottom - NavClearance;

    // Height of the free band between the bar and the gesture strip.
    internal static float ContentBandHeight(float canvasY, float safeTop, float safeBottom)
        => ContentBottom(canvasY, safeBottom) - ContentTop(safeTop);

    // Largest uniform scale that fits base content into the given box,
    // clamped so growth stays sane and shrink below authored size is
    // impossible (growing is the point; fitting is the constraint).
    internal static float FillScale(
        float baseWidth,
        float baseHeight,
        float maxWidth,
        float maxHeight,
        float maxScale)
    {
        var byWidth = baseWidth > 1f ? maxWidth / baseWidth : maxScale;
        var byHeight = baseHeight > 1f ? maxHeight / baseHeight : maxScale;
        return Math.Clamp(Math.Min(byWidth, byHeight), 1f, maxScale);
    }

    internal static float CenterX(float canvasX, float width) => (canvasX - width) * 0.5f;

    // Y for a control hanging from the bottom of the content band.
    internal static float BottomAnchoredY(float canvasY, float safeBottom, float height)
        => ContentBottom(canvasY, safeBottom) - height;

    // Scale that lifts a control's smaller side up to the touch minimum,
    // never shrinking and never exceeding maxScale.
    internal static float TouchScale(float baseWidth, float baseHeight, float maxScale)
    {
        var smallSide = Math.Min(
            baseWidth > 1f ? baseWidth : MinTouchSide,
            baseHeight > 1f ? baseHeight : MinTouchSide
        );
        return Math.Clamp(MinTouchSide / smallSide, 1f, maxScale);
    }
}
