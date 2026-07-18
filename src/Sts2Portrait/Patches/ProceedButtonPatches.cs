using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

public static class ProceedLayout
{
    public static float EdgeMargin = 14f;   // canvas units kept clear of the screen edge
    public static float FallbackWidth = 300f;
}

// The Skip / Proceed / back ribbons settle at `_showPosRatio * viewportSize`, which places
// the button's LEFT edge near the right side. On a narrow portrait canvas the button's own
// width then pushes it off-screen (you literally can't tap Skip). Clamp the target so a
// right-side button's right edge stays inside the canvas; left-side buttons are untouched.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NProceedButton), "ShowPos", MethodType.Getter)]
public static class ProceedShowPosPatch
{
    public static void Postfix(object __instance, ref Vector2 __result)
    {
        var canvas = PortraitConfig.CanvasSize;
        if (!PortraitConfig.IsPortrait(canvas)) return;
        var btn = (Control)__instance;
        float w = btn.Size.X > 1 ? btn.Size.X : ProceedLayout.FallbackWidth;
        float maxX = canvas.X - ProceedLayout.EdgeMargin - w;
        if (__result.X > maxX) __result.X = maxX;   // only pull in right-side overflow
    }
}
