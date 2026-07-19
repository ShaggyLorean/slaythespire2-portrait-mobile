using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

// The Neow intro (NAncientEventLayout) sizes its art by lerping between landscape aspect
// presets (4:3 to 21:9). A portrait canvas is far outside that range, so the art clamps to
// the 4:3 preset and the creature ends up small and half off screen with a big empty mist
// band under it. Override the container transform for portrait so Neow fills the width and
// sits in the upper part of the screen.
public static class NeowLayout
{
    public static float BgScale = 1.1f;
    public static float BgPosX = -140f;
    public static float BgPosY = -30f;
    public static float BannerYFrac = 0.30f;   // intro name card sits here instead of bottom-left
}

// The Neow name card (NAncientNameBanner) is a full-screen intro flourish with a custom
// glyph shader and a position tween. On a tall portrait canvas its text lands off in a
// corner and fights any repositioning. It is not load-bearing, so hide it in portrait; the
// creature, the dialogue and the options carry the event with no overlap.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.NAncientNameBanner), "_Ready")]
public static class NeowBannerPatch
{
    public static void Postfix(Node __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        if (__instance is CanvasItem c) c.Visible = false;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.NAncientBgContainer), "OnWindowChange")]
public static class NeowBgPatch
{
    public static void Postfix(Control __instance)
    {
        if (__instance.HasMeta("sts2p_neow")) return;
        __instance.SetMeta("sts2p_neow", true);
        Loop(__instance);
    }

    private static void Loop(Control c)
    {
        if (!GodotObject.IsInstanceValid(c) || !c.IsInsideTree()) return;
        if (PortraitConfig.IsPortrait(PortraitConfig.CanvasSize))
        {
            Tune.Reload();
            c.PivotOffset = c.Size * 0.5f;
            c.Scale = Vector2.One * NeowLayout.BgScale;
            c.Position = new Vector2(NeowLayout.BgPosX, NeowLayout.BgPosY);
        }
        c.GetTree().CreateTimer(0.4).Timeout += () => Loop(c);
    }
}
