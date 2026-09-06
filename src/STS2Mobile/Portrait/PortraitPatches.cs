using System;
using System.Collections.Generic;
using System.Linq;
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
        typeof(MainMenuReticlePatch),
        typeof(ProfileScreenPatch),
        typeof(PatchNotesOpenPatch),
        typeof(PauseMenuPatch),
        typeof(PauseMenuOpenedPatch),
        typeof(CharacterSelectInitializePatch),
        typeof(CharacterSelectOpenedPatch),
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
        typeof(ContinueRunInfoSourcePatch),
        typeof(CardDescriptionCapPatch),
        typeof(CardRewardScreenPatch),
        typeof(GridSelectTickboxPatch),
        typeof(ChooseACardScreenPatch),
        typeof(EndTurnShowPosPatch),
        typeof(PortraitFtuePatch),
        typeof(TopBarInitializePatch),
        typeof(TopBarPotionPatch),
        typeof(PotionPopupPatch),
        typeof(HoverTipAlignmentPatch),
        typeof(MapBackgroundReadyPatch),
        typeof(MapBackgroundWindowPatch),
        typeof(MapScreenReadyPatch),
        typeof(MapLegendPatch),
        typeof(MapScrollRangePatch),
        typeof(EventRoomPatch),
        typeof(AncientEventReadyPatch),
        typeof(AncientEventDialoguePatch),
        typeof(RewardsScreenPatch),
        typeof(TreasureRoomPatch),
        typeof(GameOverScreenPatch),
        typeof(CompendiumSubmenuPatch),
        typeof(ModdingScreenPatch),
        typeof(DeckViewSortRowPatch),
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
        PortraitTouchInput.Install();

        // A patch class written but never listed here silently does nothing;
        // that mistake has happened more than once, so make it loud. The
        // detector itself must never take the patch group down: GetTypes can
        // throw for types whose game references fail to resolve, so it runs
        // fully armored and settles for the loadable subset.
        try
        {
            Type[] allTypes;
            try
            {
                allTypes = typeof(PortraitPatches).Assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                allTypes = ex.Types.Where(t => t is not null).ToArray();
            }
            foreach (var type in allTypes)
            {
                if (type.Namespace != typeof(PortraitPatches).Namespace)
                    continue;
                if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0)
                    continue;
                if (Array.IndexOf(PatchTypes, type) < 0)
                    PatchHelper.Log($"[Portrait] UNREGISTERED patch class: {type.Name}");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] Registration audit skipped: {ex.GetBaseException().Message}");
        }

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
