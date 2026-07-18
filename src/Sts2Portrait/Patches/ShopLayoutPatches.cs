using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

/// <summary>
/// Dükkan (merchant) dikey yerleşimi.
/// Yapı: NMerchantInventory > SlotsContainer [TextureRect, 1747×978, anchor-center]
///   çocukları: 5+2 MerchantCard (kart), Relics(3), Potions(3), MerchantCardRemoval (servis).
/// SlotsContainer landscape için 1747px geniş → 1080 canvas'ta içerik gpos ~42→1310 arası,
/// SAĞDAKİ kartlar + servis ikonu ekran dışında (kırpık). Ayrıca içerik canvas ortasından
/// (540) sağa kaymış (~676).
///
/// Çözüm: SlotsContainer'ı merkezinden ölçekle (hepsi çocuk → hepsi küçülür) + X nudge ile
/// canvas'ta ortala. Açılış animasyonu (DoOpenAnimation) pozisyon/scale tween'leyebildiğinden
/// tween BİTTİKTEN sonra birkaç kez yeniden uygula.
/// </summary>
public static class ShopLayout
{
    public static float SlotsScale = 0.72f;    // içerik ~1268px → *0.72 ≈ 913, 1080'e rahat sığar (okunur kartlar)
    public static float BgCoverScale = 1.75f;  // tent bg'yi dikeyde kaplat (letterbox bantları kapat)
}

// Envanter açılırken (ve her güncellemede) SlotsContainer'ı sığacak şekilde ölçekle + ortala.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory), "DoOpenAnimation")]
public static class MerchantOpenScalePatch
{
    public static void Postfix(object __instance)
    {
        // Açılış animasyonu tween'lerini geçmesi için birkaç gecikmede uygula.
        var node = (Node)__instance;
        foreach (var t in new[] { 0.1, 0.4, 0.7, 1.1 })
            Schedule(node, t);
    }

    public static void Schedule(Node inv, double delay)
    {
        if (!GodotObject.IsInstanceValid(inv) || !inv.IsInsideTree()) return;
        inv.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(inv) && inv.IsInsideTree())
                ApplyScale(inv);
        };
    }

    public static void ApplyScale(Node inv)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var slots = inv.GetNodeOrNull<Control>("SlotsContainer") ?? FindByName(inv, "SlotsContainer");
        if (slots is null) return;
        // Anchor-center panel: merkez-pivot + scale → panel YERİNDE küçülür (ortalı kalır),
        // içerik ekrana sığar. Position'a DOKUNMA (açılış animasyonuyla çakışır, paneli iter).
        slots.PivotOffset = slots.Size / 2f;
        slots.Scale = Vector2.One * ShopLayout.SlotsScale;
    }

    private static Control? FindByName(Node root, string name)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Control ctrl && c.Name == name) return ctrl;
            var found = FindByName(c, name);
            if (found is not null) return found;
        }
        return null;
    }
}

// Not: satın alma sonrası yeniden ölçeklemeye gerek yok — bir slot boşalınca diğer çocukların
// scale'i (parent SlotsContainer'dan) korunur. Açılış hook'u (DoOpenAnimation) yeterli.

// Tent arka planı (BgContainer, 2560×1200) dikeyde 1080×2160 canvas'ı kaplamıyor →
// üst/alt siyah letterbox bantları. Cover ölçekle (shopkeeper ayrı node, etkilenmez).
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "_Ready")]
public static class MerchantBgCoverPatch
{
    public static void Postfix(object __instance)
    {
        var room = (Node)__instance;
        room.GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return;
            if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
            var bg = MerchantOpenScalePatchFindBg(room);
            if (bg is null) return;
            bg.PivotOffset = bg.Size / 2f;
            bg.Scale = Vector2.One * ShopLayout.BgCoverScale;
            PortraitMod.Log($"shop bg cover x{ShopLayout.BgCoverScale}");
        };
    }

    private static Control? MerchantOpenScalePatchFindBg(Node root)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Control ctrl && c.Name == "BgContainer") return ctrl;
            var found = MerchantOpenScalePatchFindBg(c);
            if (found is not null) return found;
        }
        return null;
    }
}
