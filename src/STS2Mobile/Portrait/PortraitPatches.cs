using System;
using System.Collections.Generic;
using HarmonyLib;
using STS2Mobile.Patches;

namespace STS2Mobile.Portrait;

internal static class PortraitPatches
{
    private static readonly Type[] PatchTypes =
    {
        typeof(ApplyDisplaySettingsPatch),
        typeof(GameWindowChangePatch),
        typeof(GlobalUiWindowChangePatch),
        typeof(MainMenuReadyPatch),
        typeof(MainMenuWindowChangePatch),
        typeof(CombatBackgroundWindowPatch),
        typeof(CombatBackgroundReadyPatch),
        typeof(HandFanPatch),
        typeof(PlayerHandReadyPatch),
        typeof(CombatUiPatch),
        typeof(EndTurnPatch),
        typeof(SettingsScreenOpenedPatch),
        typeof(SettingsScreenShownPatch),
        typeof(SettingsScreenClosedPatch),
        typeof(SettingsScreenHiddenPatch),
        typeof(ContinueRunInfoPatch),
        typeof(PortraitFtuePatch),
        typeof(TopBarInitializePatch),
        typeof(TopBarPotionPatch),
        typeof(MapBackgroundReadyPatch),
        typeof(MapBackgroundWindowPatch),
        typeof(MapScreenReadyPatch),
        typeof(MapLegendPatch),
        typeof(EventRoomPatch),
        typeof(MerchantOpenPatch),
        typeof(MerchantClosePatch),
        typeof(MerchantRoomPatch),
        typeof(RestSitePatch),
        typeof(NeowBannerPatch),
        typeof(NeowBackgroundPatch),
        typeof(ProceedButtonPatch),
        typeof(CharacterSelectPatch),
    };

    internal static void Apply(Harmony harmony)
    {
        PortraitDisplay.Apply();

        var failures = new List<string>();
        foreach (var type in PatchTypes)
        {
            try
            {
                new PatchClassProcessor(harmony, type).Patch();
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name}: {ex.GetBaseException().Message}");
            }
        }

        foreach (var failure in failures)
            PatchHelper.Log($"[Portrait] Patch skipped: {failure}");

        var applied = PatchTypes.Length - failures.Count;
        PatchHelper.Log($"[Portrait] Applied {applied}/{PatchTypes.Length} layout patch classes");
        if (applied == 0)
            throw new InvalidOperationException("No portrait patch class could be applied");
    }
}
