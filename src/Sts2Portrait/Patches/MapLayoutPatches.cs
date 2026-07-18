using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Sts2Portrait.Patches;

/// <summary>
/// Harita arka planı (mağara) dikeyde sadece üst ~%50'yi kaplıyor, altı kapkara.
/// Haritanın EN ARKASINA, oyunun kendi mağara texture'ıyla (MapMid) tam-ekran, KAYMAYAN
/// bir dolgu ekliyoruz → siyah kaybolur, sert sınır olmaz (aynı doku ailesi).
/// </summary>
[HarmonyPatch(typeof(NMapScreen), "_Ready")]
public static class MapBgFillPatch
{
    private const string FillName = "Sts2PortraitMapBgFill";

    public static void Postfix(NMapScreen __instance)
    {
        TryFill(__instance, 0);
    }

    private static void TryFill(NMapScreen screen, int attempt)
    {
        screen.GetTree().CreateTimer(0.3).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree()) return;
            if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;

            // Mağara texture'ı OnVisibilityChanged'de set edilir; yüklenene kadar ~10 kez dene.
            var mapMid = screen.GetNodeOrNull<TextureRect>("TheMap/MapBg/MapMid");
            var tex = mapMid?.Texture;

            var existing = screen.GetNodeOrNull<Control>(FillName);
            if (existing is null)
            {
                var fill = new ColorRect
                {
                    Name = FillName,
                    Color = new Color(0.035f, 0.086f, 0.129f), // koyu mağara-teal (yedek)
                    AnchorRight = 1f, AnchorBottom = 1f,
                    OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    ZIndex = -100,
                };
                screen.AddChild(fill);
                screen.MoveChild(fill, 0);
                existing = fill;
            }

            // Texture geldiyse, ColorRect'in üstüne texture katmanı ekle (native mağara görünümü).
            if (tex is not null && screen.GetNodeOrNull(FillName + "Tex") is null)
            {
                var texFill = new TextureRect
                {
                    Name = FillName + "Tex",
                    Texture = tex,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    AnchorRight = 1f, AnchorBottom = 1f,
                    OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    ZIndex = -99,
                    Modulate = new Color(0.75f, 0.75f, 0.75f), // hafif karart (arkada dursun)
                };
                screen.AddChild(texFill);
                screen.MoveChild(texFill, 1);
                PortraitMod.Log("map bg fill: texture layer added");
                return;
            }

            if (tex is null && attempt < 10)
                TryFill(screen, attempt + 1); // texture'ı bekle
            else if (tex is null)
                PortraitMod.Log("map bg fill: texture never loaded, color only");
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
