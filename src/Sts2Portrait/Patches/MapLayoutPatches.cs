using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Sts2Portrait.Patches;

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
        var mid = bg.GetNodeOrNull<TextureRect>("MapMid");
        if (mid is not null)
        {
            mid.CustomMinimumSize = new Vector2(0, 2400f);
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

            var fill = new ColorRect
            {
                Name = FillName,
                Color = new Color(0.11f, 0.13f, 0.10f),
                AnchorRight = 1f, AnchorBottom = 1f,
                OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            screen.AddChild(fill);
            screen.MoveChild(fill, 0);
            PortraitMod.Log("map bg fill: colorrect added");
        };
    }
}

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
