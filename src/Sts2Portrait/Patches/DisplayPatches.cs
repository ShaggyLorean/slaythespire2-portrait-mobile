using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2Portrait.Patches;

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
