using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

/// <summary>
/// Savaş ekranı dikey yerleşimi.
/// - NCombatSceneContainer: bg dikeyde 0.9 ölçekle kalıyor (üstte siyah bant) → cover ölçek.
/// - NPlayerHand: el kartları canvas dibinde kesiliyor → CardHolderContainer'ı yukarı al.
/// </summary>
public static class CombatLayout
{
    // Deneyle ayarlanacak değerler. Bg art 2765x1296; 2160 yüksekliği kaplamak için ~1.67.
    public static float BgScale = 1.7f;        // savaş arka planı dikeyi kaplasın
    public static float HandRaise = 170f;      // el kartlarını yukarı taşı (px)
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCombatSceneContainer), "OnWindowChange")]
public static class CombatBgWindowChangePatch
{
    public static void Postfix(object __instance)
    {
        ScaleBg(__instance);
    }

    public static void ScaleBg(object instance)
    {
        var node = (Node)instance;
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var bg = Traverse.Create(instance).Field("_bgContainer").GetValue<Control>();
        if (bg is null) return;
        bg.Scale = Vector2.One * CombatLayout.BgScale;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCombatSceneContainer), "_Ready")]
public static class CombatBgReadyPatch
{
    public static void Postfix(object __instance)
    {
        // _Ready'de _bgContainer henüz atanmış olmayabilir; kısa gecikmeyle uygula.
        var node = (Node)__instance;
        node.GetTree().CreateTimer(0.1).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                CombatBgWindowChangePatch.ScaleBg(__instance);
        };
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand), "_Ready")]
public static class HandRaisePatch
{
    public static void Postfix(object __instance)
    {
        var hand = (Node)__instance;
        hand.GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree()) return;
            if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
            var container = Traverse.Create(__instance).Property("CardHolderContainer").GetValue<Control>();
            if (container is null) return;
            container.Position -= new Vector2(0, CombatLayout.HandRaise);
            PortraitMod.Log($"hand raised by {CombatLayout.HandRaise}");
        };
    }
}
