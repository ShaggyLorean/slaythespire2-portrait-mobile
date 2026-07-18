using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2Portrait.Patches;

[HarmonyPatch(typeof(NGame), "ApplyDisplaySettings")]
public static class ApplyDisplaySettingsPatch
{
    public static void Postfix() => PortraitDisplay.ApplyPortrait();
}

// NGame.OnWindowChange emits WindowChange, which drives the aspect-ratio content-scale
// (1680x1260 etc.). On portrait we own the canvas, so skip its cascade and apply ours.
[HarmonyPatch(typeof(NGame), "OnWindowChange")]
public static class NGameWindowChangePatch
{
    public static bool Prefix() => !PortraitDisplay.ApplyPortrait();
}

[HarmonyPatch(typeof(NGlobalUi), "OnWindowChange")]
public static class GlobalUiWindowChangePatch
{
    public static bool Prefix() => !PortraitDisplay.ApplyPortrait();
}
