using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Sts2Portrait.Patches;

/// <summary>
/// Karakter seçim bilgi paneli landscape'e göre x=-100'de (sol kenardan 100px taşar, metin kırpık).
/// SelectCharacter paneli _infoPanel.Position'a tween'liyor; postfix'le tween'i kesip ekran içine alırız.
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
public static class CharSelectInfoPanelPatch
{
    public const float MarginX = 40f;

    public static void Postfix(NCharacterSelectScreen __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;

        var panel = Traverse.Create(__instance).Field("_infoPanel").GetValue<Control>();
        if (panel is null) return;
        if (panel.Position.X >= MarginX) return; // zaten yerinde

        // Tween paneli -100'e götürecek; kesip doğrudan ekran içine yerleştir.
        Traverse.Create(__instance).Field("_infoPanelTween").GetValue<Tween>()?.Kill();
        panel.Position = new Vector2(MarginX, panel.Position.Y);
        Traverse.Create(__instance).Field("_infoPanelPosFinalVal").SetValue(panel.Position);
        PortraitMod.Log($"charselect info panel -> x={MarginX}");
    }
}
