using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Sts2Portrait.Patches;

/// <summary>
/// KÖK SEBEP: map_screen.tscn, MapTop/MapMid/MapBot'a stretch_mode=KeepAspectCentered veriyor.
/// Portrait genişliğinde (~1129) 2036x1440 parşömen 1129x799'a aspect-fit edilip 1080 slot'ta
/// ORTALANIYOR → her bölümün üst/altında ~141px şeffaf bant → bölümler arası ~281px SİYAH gap.
/// FIX: KeepAspectCovered — texture 1080 yüksekliği tam kaplar (landscape'in ürettiği 1527x1080 ile
/// aynı), yırtık yan kenarlar kırpılır, komşu opak kenarlar dikişsiz buluşur → sürekli parşömen.
/// Node-güvenli: sadece TextureRect'in nasıl boyandığını değiştirir, hiçbir node geometrisine dokunmaz.
/// </summary>
[HarmonyPatch(typeof(NMapBg), "_Ready")]
public static class MapBgSeamFixPatch
{
    private static readonly string[] Sections = { "MapTop", "MapMid", "MapBot" };

    public static void Postfix(NMapBg __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        ApplyCovered(__instance);
    }

    public static void ApplyCovered(NMapBg bg)
    {
        foreach (var name in Sections)
        {
            var tr = bg.GetNodeOrNull<TextureRect>(name);
            if (tr is null) continue;
            tr.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        }
        // Parşömen dikeyde sadece üst yarıyı kaplıyor; MapMid'i uzatıp alt node'ları da
        // parşömenle kapla (node'lar ayrı olduğundan hizalama bozulmaz).
        var mid = bg.GetNodeOrNull<TextureRect>("MapMid");
        if (mid is not null)
        {
            mid.CustomMinimumSize = new Vector2(0, 2400f); // 1080 → 2400 (VBox ~1320 uzar)
            mid.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }
        PortraitMod.Log("map bg: KeepAspectCovered + MapMid uzatıldı");
    }
}

[HarmonyPatch(typeof(NMapBg), "OnWindowChange")]
public static class MapBgSeamReassertPatch
{
    public static void Postfix(NMapBg __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        MapBgSeamFixPatch.ApplyCovered(__instance);
    }
}

/// <summary>
/// Harita parşömen scroll'u yatay tasarlandığından dikey ekranda etrafı/altı kapkara kalıyor.
/// Haritanın EN ARKASINA tam-ekran, KAYMAYAN, oyunun mağara temasına uygun bir DİKEY GRADYAN
/// ekliyoruz → siyah kaybolur, yamalı görünmez, kasıtlı bir mağara zemini gibi durur.
/// (Parşömen texture'ı MapMid'i kullanmak YANLIŞTI: o parşömen, mağara değil.)
/// </summary>
[HarmonyPatch(typeof(NMapScreen), "_Ready")]
public static class MapBgFillPatch
{
    private const string FillName = "Sts2PortraitMapBgFill";

    public static void Postfix(NMapScreen __instance)
    {
        var screen = __instance;
        screen.GetTree().CreateTimer(0.2).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree()) return;
            if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
            if (screen.GetNodeOrNull(FillName) is not null) return;

            // Görünür koyu mağara tonu (siyah değil) — parşömen ötesi alan kasıtlı dursun.
            // ColorRect garanti render eder (GradientTexture2D bazen görünmüyordu).
            var fill = new ColorRect
            {
                Name = FillName,
                Color = new Color(0.11f, 0.13f, 0.10f), // koyu yeşil-kahve mağara ~srgb(28,33,26)
                AnchorRight = 1f, AnchorBottom = 1f,
                OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            screen.AddChild(fill);
            screen.MoveChild(fill, 0); // en arkaya (TheMap ve legend önde kalır)
            PortraitMod.Log("map bg fill: colorrect added");
        };
    }
}

/// <summary>
/// Harita legend'ı: oyunun MapLegendX => Size.X * 0.8f (=903) değeri, 340 geniş legend'ı
/// canvas dışına (sağ kenar 1243 > 1129) itiyor. Getter'ı clamp'liyoruz.
/// </summary>
[HarmonyPatch(typeof(NMapScreen), "MapLegendX", MethodType.Getter)]
public static class MapLegendXPatch
{
    private const float LegendWidth = 340f;
    private const float Margin = 20f;

    public static void Postfix(NMapScreen __instance, ref float __result)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        float maxX = __instance.Size.X - LegendWidth - Margin;
        if (__result > maxX) __result = maxX;
    }
}
