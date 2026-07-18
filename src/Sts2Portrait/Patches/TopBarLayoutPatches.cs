using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace Sts2Portrait.Patches;

/// <summary>
/// Üst HUD bar landscape tasarımlı. Yapı: TopBar > LeftAlignedStuff [HBox, ~1280 geniş:
/// Portrait+HP+Gold+Potion+RoomIcons+Modifiers] ve RightAlignedStuff [HBox, sağa-yaslı:
/// Timer+Map+Deck+Pause]. Portrait 1080'de LeftAlignedStuff taşıp RightAlignedStuff'la çakışıyor.
///
/// HBox'lar çocuklarını AUTO-LAYOUT yaptığından tek tek eleman konumlamak işe yaramaz.
/// Bunun yerine CONTAINER'ları konumluyoruz:
///  - LeftAlignedStuff'ı satır 1'e, genişliğe SIĞACAK şekilde ölçekle (potion/modifier arttıkça
///    genişler → ölçek dinamik küçülür ama HER ZAMAN sığar, çakışmaz — max durumu bile görünür).
///  - RightAlignedStuff'ı satır 2'ye, sağa-yaslı taşı.
/// </summary>
public static class TopBarLayout
{
    public static float MarginL = 12f, MarginR = 12f;
    public static float Row1Y = 6f;
    public static float Row2Y = 96f;
}

public static class TopBarReflow
{
    public static void Apply(NTopBar bar)
    {
        if (!GodotObject.IsInstanceValid(bar)) return;
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        float W = PortraitConfig.CanvasSize.X;

        var left = bar.GetNodeOrNull<Control>("LeftAlignedStuff");
        var right = bar.GetNodeOrNull<Control>("RightAlignedStuff");

        // --- Satır 1: LeftAlignedStuff, genişliğe sığacak şekilde ölçekle ---
        if (left is not null)
        {
            float avail = W - TopBarLayout.MarginL - TopBarLayout.MarginR;
            float contentW = left.Size.X > 1 ? left.Size.X : 1280f;
            float fit = Mathf.Min(1f, avail / contentW);
            left.PivotOffset = Vector2.Zero;              // sol-üstten ölçekle
            left.AnchorLeft = left.AnchorTop = left.AnchorRight = left.AnchorBottom = 0f;
            left.Scale = new Vector2(fit, fit);
            left.Position = new Vector2(TopBarLayout.MarginL, TopBarLayout.Row1Y);
        }

        // --- Satır 2: RightAlignedStuff (Map/Deck/Pause), sağa-yaslı ---
        if (right is not null)
        {
            float rw = right.Size.X > 1 ? right.Size.X : 476f;
            float rfit = Mathf.Min(1f, (W - TopBarLayout.MarginL - TopBarLayout.MarginR) / rw);
            right.PivotOffset = Vector2.Zero;
            right.AnchorLeft = right.AnchorTop = right.AnchorRight = right.AnchorBottom = 0f;
            right.Scale = new Vector2(rfit, rfit);
            right.Position = new Vector2(W - TopBarLayout.MarginR - rw * rfit, TopBarLayout.Row2Y);
        }

        PortraitMod.Log($"topbar reflow: W={W} leftW={(left?.Size.X ?? 0):F0} rightW={(right?.Size.X ?? 0):F0}");
    }

    /// <summary>Herhangi bir node'dan yukarı yürüyerek NTopBar ata bul.</summary>
    public static NTopBar? FindTopBar(Node from)
    {
        for (Node? n = from; n is not null; n = n.GetParent())
            if (n is NTopBar tb) return tb;
        return null;
    }
}

// (1) İlk reflow — refs + başlangıç içerik oluştuktan sonra.
[HarmonyPatch(typeof(NTopBar), "Initialize")]
public static class TopBarInitReflowPatch
{
    public static void Postfix(NTopBar __instance) => Defer(__instance, 0);

    // İçerik boyutu ilk karede 0 olabilir → birkaç kez dene ki gerçek genişliğe göre ölçeklensin.
    public static void Defer(NTopBar bar, int attempt)
    {
        bar.GetTree().CreateTimer(0.08).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(bar) || !bar.IsInsideTree()) return;
            TopBarReflow.Apply(bar);
            if (attempt < 4) Defer(bar, attempt + 1);
        };
    }
}

// (2) Belt boyutu değişince yeniden ölçekle.
[HarmonyPatch(typeof(NTopBar), "MaxPotionsChanged")]
public static class TopBarMaxPotionsReflowPatch
{
    public static void Postfix(NTopBar __instance) => TopBarInitReflowPatch.Defer(__instance, 3);
}

// (3) Yeni potion holder eklenince yeniden ölçekle (LeftAlignedStuff genişledi).
[HarmonyPatch(typeof(NPotionContainer), "GrowPotionHolders")]
public static class PotionGrowReflowPatch
{
    public static void Postfix(NPotionContainer __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var bar = TopBarReflow.FindTopBar(__instance);
        if (bar is not null) TopBarInitReflowPatch.Defer(bar, 3);
    }
}
