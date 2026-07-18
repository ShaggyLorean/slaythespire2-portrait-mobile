using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Sts2Portrait.Patches;

[HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
public static class CharSelectInfoPanelPatch
{
    public const float MarginX = 40f;

    public static void Postfix(NCharacterSelectScreen __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;

        var panel = Traverse.Create(__instance).Field("_infoPanel").GetValue<Control>();
        if (panel is null) return;
        if (panel.Position.X >= MarginX) return;

        Traverse.Create(__instance).Field("_infoPanelTween").GetValue<Tween>()?.Kill();
        panel.Position = new Vector2(MarginX, panel.Position.Y);
        Traverse.Create(__instance).Field("_infoPanelPosFinalVal").SetValue(panel.Position);
        PortraitMod.Log($"charselect info panel -> x={MarginX}");
    }
}
