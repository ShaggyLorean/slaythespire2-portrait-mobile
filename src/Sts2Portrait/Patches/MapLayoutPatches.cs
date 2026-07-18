using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Sts2Portrait.Patches;

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

            // Dikey mağara gradyanı: üstte hafif daha aydınlık, altta koyulaşan koyu yeşil-teal.
            var grad = new Gradient();
            grad.SetColor(0, new Color(0.06f, 0.10f, 0.11f));  // üst
            grad.SetColor(1, new Color(0.02f, 0.045f, 0.05f)); // alt
            grad.AddPoint(0.5f, new Color(0.04f, 0.07f, 0.075f)); // orta

            var gtex = new GradientTexture2D
            {
                Gradient = grad,
                Fill = GradientTexture2D.FillEnum.Linear,
                FillFrom = new Vector2(0.5f, 0f),
                FillTo = new Vector2(0.5f, 1f),
                Width = 8, Height = 256,
            };

            var fill = new TextureRect
            {
                Name = FillName,
                Texture = gtex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                AnchorRight = 1f, AnchorBottom = 1f,
                OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            screen.AddChild(fill);
            screen.MoveChild(fill, 0); // en arkaya (TheMap ve legend önde kalır)
            PortraitMod.Log("map bg fill: gradient added");
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
