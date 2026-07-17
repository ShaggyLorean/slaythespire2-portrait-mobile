using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2Portrait.Patches;

/// <summary>
/// Dikey pencere/canvas patch'leri.
///
/// - NGame.ApplyDisplaySettings: oyunun display kurulumu (aspect ratio switch'i,
///   fullscreen) çalıştıktan HEMEN SONRA dikey canvas ölçeğimizi dayatır.
/// - NGlobalUi.OnWindowChange: oyun her resize'da ContentScaleSize'ı kendi oran
///   mantığıyla ezer; postfix ile dikey ölçeği yeniden uygularız.
/// </summary>
[HarmonyPatch(typeof(NGame), "ApplyDisplaySettings")]
public static class ApplyDisplaySettingsPatch
{
    public static void Postfix() => PortraitDisplay.ApplyPortrait();
}

[HarmonyPatch(typeof(NGlobalUi), "OnWindowChange")]
public static class GlobalUiWindowChangePatch
{
    public static void Postfix() => PortraitDisplay.ApplyPortrait();
}
