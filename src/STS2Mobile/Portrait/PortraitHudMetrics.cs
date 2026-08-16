using System;

namespace STS2Mobile.Portrait;

// Single source of truth for the vertical band the portrait run HUD occupies.
// PortraitTopBar lays the HUD out from these values, and content patches for
// non-combat screens use ContentTop to start below the HP/gold/potion/relic
// stack instead of underneath it (BUG-003). Keep this class free of game and
// Godot types so the PC harness can compile and unit-test the math.
internal static class PortraitHudMetrics
{
    // Distance from the safe-area top to the first HUD row.
    internal const float HudTopPadding = 24f;

    // Row pitch of the left HUD column, relative to HudTop.
    internal const float GoldRowOffset = 92f;
    internal const float PotionRowOffset = 184f;
    internal const float RoomRowOffset = 286f;
    internal const float RelicRowOffset = 394f;

    // The relic row is the lowest HUD element: 68px icons at up to 1.48 scale.
    internal const float RelicRowHeight = 104f;

    // Breathing room between the HUD stack and the first content line.
    internal const float ContentMargin = 16f;

    // Combat top band: must always cover the authored blue-grey sky strip of
    // the landscape background art (fixed art height), and grow to back the
    // display cutout when its inset reaches deeper than the art strip.
    internal const float CombatArtStripCover = 118f;
    internal const float CombatTopBandExtra = 6f;

    internal static float HudTop(float safeTop) => safeTop + HudTopPadding;

    // Bottom edge of the HUD stack (relic row included), in canvas units.
    internal static float HudBottom(float safeTop)
        => HudTop(safeTop) + RelicRowOffset + RelicRowHeight;

    // First Y where non-combat screen content may start.
    internal static float ContentTop(float safeTop) => HudBottom(safeTop) + ContentMargin;

    internal static float CombatTopBandHeight(float safeTop)
        => Math.Max(CombatArtStripCover, safeTop + CombatTopBandExtra);
}
