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

    // How far the parchment is pulled up past the screen top. The sheet's torn top edge
    // plus the dark void above it read as a broken strip under the HUD on phones, so the
    // edge art is pushed off screen and the solid paper runs from the very top.
    public static float TopOverscan = 260f;

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
        if (!bg.HasMeta("sts2p_overscan"))
        {
            bg.SetMeta("sts2p_overscan", true);
            bg.Position = new Vector2(bg.Position.X, bg.Position.Y - TopOverscan);
        }
        PortraitMod.Log("map bg: covered, extended, overscanned");
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

// The node columns come out of the layout with uneven side margins on a narrow canvas
// (the leftmost column nearly touches the edge). Measure the real spread and shift the
// point and path layers so the whole graph sits centered.
[HarmonyPatch(typeof(NMapScreen), "_Ready")]
public static class MapCenterPatch
{
    public static void Postfix(NMapScreen __instance)
    {
        var screen = __instance;
        foreach (var delay in new[] { 0.3, 1.0, 2.5 })
        {
            screen.GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree()) return;
                var canvas = PortraitConfig.CanvasSize;
                if (!PortraitConfig.IsPortrait(canvas)) return;
                Center(screen, canvas.X);
            };
        }
    }

    private static void Center(Node screen, float width)
    {
        var points = ShopReflow.FindControl(screen, "Points");
        var paths = ShopReflow.FindControl(screen, "Paths");
        if (points is null) return;

        float min = float.MaxValue, max = float.MinValue;
        foreach (var child in points.GetChildren())
        {
            if (child is not Control c || c.Name.ToString().Contains("Vote")) continue;
            min = Mathf.Min(min, c.Position.X);
            max = Mathf.Max(max, c.Position.X + c.Size.X);
        }
        if (min > max) return;

        float dx = (width - (max - min)) / 2f - min;
        points.Position = new Vector2(dx, points.Position.Y);
        if (paths is not null) paths.Position = new Vector2(dx, paths.Position.Y);
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
