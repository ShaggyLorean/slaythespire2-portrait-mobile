using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace Sts2Portrait.Patches;

/// <summary>
/// Üst HUD bar landscape tasarımlı (1680-2580 geniş); portrait 1080'de öğeler çakışıyor
/// (süre Map/Deck üstüne biniyor) ve potion satırı slot arttıkça taşıyor.
/// Kendi 2-satırlı reflow'umuzu dayatıyoruz; potion sayısı değiştikçe yeniden paketliyoruz.
/// </summary>
public static class TopBarLayout
{
    public static float MarginL = 24f, MarginR = 24f;
    public static float Row1Y = 8f, Row2Y = 78f;
    public static float SlotW = 60f, SlotGap = 12f;
    public static float IconW = 62f, IconGap = 8f;
    public static float BtnW = 80f, BtnGap = 8f;
    public static float LeftGap = 14f;

    // Boyut henüz atanmamışsa (deferred anında) yedek genişlikler.
    public static float WidthOr(Control c, float fallback) => c.Size.X > 1 ? c.Size.X : fallback;

    public static void Flatten(Control c)
    {
        if (c is null) return;
        c.AnchorLeft = c.AnchorTop = c.AnchorRight = c.AnchorBottom = 0f;
        c.GrowHorizontal = Control.GrowDirection.End;
        c.GrowVertical = Control.GrowDirection.End;
    }
}

public static class TopBarReflow
{
    public static void Apply(NTopBar bar)
    {
        if (!GodotObject.IsInstanceValid(bar)) return;
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        float W = PortraitConfig.CanvasSize.X;

        // --- Satır 1 sol: Portrait, Hp, Gold ---
        float x = TopBarLayout.MarginL;
        PlaceRow1(bar.Portrait, ref x, 72f);
        PlaceRow1(bar.Hp, ref x, 179f);
        PlaceRow1(bar.Gold, ref x, 138f);
        float leftEnd = x;

        // --- Satır 1 sağ: Map, Deck, Pause (sağdan içe) ---
        float rx = W - TopBarLayout.MarginR;
        PlaceRow1Right(bar.Map, ref rx);
        PlaceRow1Right(bar.Deck, ref rx);
        PlaceRow1Right(bar.Pause, ref rx);
        float rightStart = rx;

        // --- Süre: satır 1 ortasındaki boşluğa (çakışmaz) ---
        var timer = bar.Timer;
        if (timer is not null)
        {
            TopBarLayout.Flatten(timer);
            float tw = TopBarLayout.WidthOr(timer, 164f);
            timer.Position = new Vector2((leftEnd + rightStart) * 0.5f - tw * 0.5f, TopBarLayout.Row1Y);
        }

        // --- Satır 2 sağ: Boss/Floor/Room ikonları (sağdan içe) ---
        float irx = W - TopBarLayout.MarginR;
        PlaceIconRight(bar.BossIcon, ref irx);
        PlaceIconRight(bar.FloorIcon, ref irx);
        PlaceIconRight(bar.RoomIcon, ref irx);

        // --- Satır 2 sol: potion satırı, kalan alanı doldurur ---
        PackPotions(bar.PotionContainer, TopBarLayout.MarginL, irx - TopBarLayout.IconGap);

        PortraitMod.Log($"topbar reflow: W={W} leftEnd={leftEnd:F0} rightStart={rightStart:F0}");
    }

    private static void PlaceRow1(Control c, ref float x, float fallbackW)
    {
        if (c is null) return;
        TopBarLayout.Flatten(c);
        c.Position = new Vector2(x, TopBarLayout.Row1Y);
        x += TopBarLayout.WidthOr(c, fallbackW) + TopBarLayout.LeftGap;
    }

    private static void PlaceRow1Right(Control c, ref float rx)
    {
        if (c is null) return;
        TopBarLayout.Flatten(c);
        rx -= TopBarLayout.BtnW;
        c.Position = new Vector2(rx, TopBarLayout.Row1Y);
        rx -= TopBarLayout.BtnGap;
    }

    private static void PlaceIconRight(Control c, ref float rx)
    {
        if (c is null || !c.Visible) return;
        TopBarLayout.Flatten(c);
        rx -= TopBarLayout.IconW;
        c.Position = new Vector2(rx, TopBarLayout.Row2Y);
        rx -= TopBarLayout.IconGap;
    }

    // Potion satırını canlı slot sayısına göre paketle → her zaman sığar, sadece aralık daralır.
    public static void PackPotions(NPotionContainer pc, float startX, float endX)
    {
        if (pc is null) return;
        TopBarLayout.Flatten(pc);
        pc.Position = new Vector2(startX, TopBarLayout.Row2Y);

        var holdersRoot = pc.GetNodeOrNull<Control>("MarginContainer/PotionHolders");
        var holders = Traverse.Create(pc).Field("_holders").GetValue<IList>();
        int n = holders?.Count ?? 0;
        if (n == 0 || holdersRoot is null) return;

        float avail = endX - startX;
        float step = Mathf.Min(TopBarLayout.SlotW + TopBarLayout.SlotGap, avail / n);
        step = Mathf.Max(step, TopBarLayout.SlotW * 0.85f);

        holdersRoot.Position = Vector2.Zero;
        for (int i = 0; i < n; i++)
        {
            if (holders[i] is Control h)
            {
                TopBarLayout.Flatten(h);
                h.Position = new Vector2(i * step, 0f);
            }
        }
    }

    /// <summary>Herhangi bir node'dan yukarı yürüyerek NTopBar ata bul.</summary>
    public static NTopBar? FindTopBar(Node from)
    {
        for (Node? n = from; n is not null; n = n.GetParent())
            if (n is NTopBar tb) return tb;
        return null;
    }
}

// (1) İlk tam reflow — refs + başlangıç potion'ları oluştuktan sonra.
[HarmonyPatch(typeof(NTopBar), "Initialize")]
public static class TopBarInitReflowPatch
{
    public static void Postfix(NTopBar __instance) => Defer(__instance);

    public static void Defer(NTopBar bar)
    {
        bar.GetTree().CreateTimer(0.06).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(bar) && bar.IsInsideTree())
                TopBarReflow.Apply(bar);
        };
    }
}

// (2) Belt boyutu değişince tüm bar yeniden paketlensin.
[HarmonyPatch(typeof(NTopBar), "MaxPotionsChanged")]
public static class TopBarMaxPotionsReflowPatch
{
    public static void Postfix(NTopBar __instance) => TopBarInitReflowPatch.Defer(__instance);
}

// (3) Yeni potion holder eklenince potion satırını yeniden paketle.
[HarmonyPatch(typeof(NPotionContainer), "GrowPotionHolders")]
public static class PotionGrowReflowPatch
{
    public static void Postfix(NPotionContainer __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var bar = TopBarReflow.FindTopBar(__instance);
        if (bar is not null)
            bar.GetTree().CreateTimer(0.02).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(bar)) TopBarReflow.Apply(bar);
            };
    }
}
