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
}
