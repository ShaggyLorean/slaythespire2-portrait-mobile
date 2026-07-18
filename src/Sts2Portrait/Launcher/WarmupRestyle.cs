using System;
using Godot;
using HarmonyLib;

namespace Sts2Portrait.Launcher;

// The shader warmup screen comes from the mobile launcher and looks nothing like the
// rest of our shell. We keep its scanning logic untouched and only reskin what it
// builds: swap the background for ours, restyle the panel, labels and progress bar,
// and center the panel on a portrait screen.
public static class WarmupRestyle
{
    public static void TryInstall(Harmony harmony)
    {
        var t = AccessTools.TypeByName("STS2Mobile.Launcher.ShaderWarmupScreen");
        if (t is null) return;
        var init = AccessTools.Method(t, "Initialize");
        if (init is null) return;
        harmony.Patch(init, postfix: new HarmonyMethod(typeof(WarmupRestyle), nameof(InitializePostfix)));
        PortraitMod.Log("warmup restyle installed");
    }

    public static void InitializePostfix(Control __instance)
    {
        try { Restyle(__instance); }
        catch (Exception e) { PortraitMod.Log("warmup restyle: " + e.Message); }
    }

    private static void Restyle(Control root)
    {
        var vp = root.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1080, 2160);
        root.Position = Vector2.Zero;
        root.Size = vp;

        Control panel = null;
        foreach (var child in root.GetChildren())
        {
            var name = child.GetType().Name;
            if (name == "ScreenBackground" && child is CanvasItem bg)
                bg.Visible = false;
            else if (name == "StyledPanel" && child is Control p)
                panel = p;
        }

        var ourBg = PortraitTheme.Background();
        root.AddChild(ourBg);
        root.MoveChild(ourBg, 0);

        if (panel is null) return;

        var box = new StyleBoxFlat { BgColor = PortraitTheme.PanelFill };
        box.SetCornerRadiusAll(20);
        box.SetContentMarginAll(34);
        box.BorderColor = PortraitTheme.GoldDim;
        box.SetBorderWidthAll(2);
        if (panel is PanelContainer or Panel)
            panel.AddThemeStyleboxOverride("panel", box);

        RestyleTree(panel, 0);
        panel.Position = new Vector2((vp.X - panel.Size.X) / 2f, (vp.Y - panel.Size.Y) / 2f);
    }

    private static void RestyleTree(Node n, int labelIndex)
    {
        foreach (var child in n.GetChildren())
        {
            if (child is Label l)
            {
                bool heading = l.GetThemeFontSize("font_size") >= 30 || labelIndex == 0;
                l.AddThemeColorOverride("font_color", heading ? PortraitTheme.Gold : PortraitTheme.Muted);
                l.Modulate = Colors.White;
                labelIndex++;
            }
            else if (child is ProgressBar p)
            {
                PortraitTheme.StyleProgress(p);
            }
            RestyleTree(child, labelIndex);
        }
    }
}
