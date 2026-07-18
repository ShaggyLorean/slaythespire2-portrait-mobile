using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace Sts2Portrait.Patches;

public static class TopBarLayout
{
    public static float MarginL = 12f, MarginR = 12f;
    public static float Row1Y = 6f;
    public static float Row2Y = 96f;
    public static float RelicMaxWFrac = 0.52f;
    public static float RelicY = 102f;
    public static float WatermarkY = 182f;
    public static float WatermarkScale = 0.55f;
}

public static class TopBarReflow
{
    private static string _lastLog = "";
    private static Control? _watermark;

    public static void Apply(NTopBar bar)
    {
        Tune.Reload();
        if (!GodotObject.IsInstanceValid(bar)) return;
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        float W = PortraitConfig.CanvasSize.X;
        float safe = PortraitConfig.SafeTop();   // keep clear of notch / punch-hole camera
        float row1 = TopBarLayout.Row1Y + safe;
        float row2 = TopBarLayout.Row2Y + safe;

        var left = bar.GetNodeOrNull<Control>("LeftAlignedStuff");
        var right = bar.GetNodeOrNull<Control>("RightAlignedStuff");

        if (left is not null)
        {
            float avail = W - TopBarLayout.MarginL - TopBarLayout.MarginR;
            float contentW = left.Size.X > 1 ? left.Size.X : 1280f;
            float fit = Mathf.Min(1f, avail / contentW);
            left.PivotOffset = Vector2.Zero;
            left.AnchorLeft = left.AnchorTop = left.AnchorRight = left.AnchorBottom = 0f;
            left.Scale = new Vector2(fit, fit);
            left.Position = new Vector2(TopBarLayout.MarginL, row1);
        }

        if (right is not null)
        {
            float rw = right.Size.X > 1 ? right.Size.X : 476f;
            float rfit = Mathf.Min(1f, (W - TopBarLayout.MarginL - TopBarLayout.MarginR) / rw);
            right.PivotOffset = Vector2.Zero;
            right.AnchorLeft = right.AnchorTop = right.AnchorRight = right.AnchorBottom = 0f;
            right.Scale = new Vector2(rfit, rfit);
            right.Position = new Vector2(W - TopBarLayout.MarginR - rw * rfit, row2);
        }

        var parent = bar.GetParent();
        var relicInv = parent is null ? null : ShopReflow.FindControl(parent, "RelicInventory");
        if (relicInv is not null)
        {
            int n = 0;
            foreach (var c in relicInv.GetChildren()) if (c is Control cc && cc.Visible) n++;
            float contentW = 12f + n * 68f;
            float rightStart = right?.Position.X ?? W * 0.55f;
            float maxW = Mathf.Min(W * TopBarLayout.RelicMaxWFrac + 0f,
                                   rightStart - TopBarLayout.MarginL - 16f);
            float fitR = Mathf.Min(1f, maxW / Mathf.Max(68f, contentW));
            relicInv.PivotOffset = Vector2.Zero;
            relicInv.Scale = new Vector2(fitR, fitR);
            relicInv.Position = new Vector2(TopBarLayout.MarginL, TopBarLayout.RelicY + safe);
        }

        FixWatermark(bar);

        var log = $"topbar reflow: W={W} leftW={(left?.Size.X ?? 0):F0} rightW={(right?.Size.X ?? 0):F0} " +
                  $"relics={(relicInv?.GetChildCount() ?? 0)} wm={(_watermark is not null)}";
        if (log != _lastLog) { _lastLog = log; PortraitMod.Log(log); }
    }

    private static int _wmTick;
    private static void FixWatermark(Node any)
    {
        if (_watermark is null || !GodotObject.IsInstanceValid(_watermark))
        {
            if (_wmTick++ % 6 != 0) return;
            _watermark = FindWatermark(any.GetTree().Root);
            if (_watermark is not null)
                PortraitMod.Log($"watermark: size={_watermark.Size} anchors=({_watermark.AnchorLeft},{_watermark.AnchorTop})");
        }
        if (_watermark is null) return;
        _watermark.PivotOffset = new Vector2(_watermark.Size.X, 0f);
        _watermark.Scale = Vector2.One * TopBarLayout.WatermarkScale;
        _watermark.Position = new Vector2(_watermark.Position.X, TopBarLayout.WatermarkY);
    }

    private static Control? FindWatermark(Node n)
    {
        string? text = n switch
        {
            Label l => l.Text,
            RichTextLabel rl => rl.Text,
            _ => null,
        };
        if (text is not null && (text.Contains("MODDED") || text.Contains("(v0.")))
            return n.GetParent() as BoxContainer ?? (Control)n;
        foreach (var child in n.GetChildren())
        {
            var r = FindWatermark(child);
            if (r is not null) return r;
        }
        return null;
    }

    public static NTopBar? FindTopBar(Node from)
    {
        for (Node? n = from; n is not null; n = n.GetParent())
            if (n is NTopBar tb) return tb;
        return null;
    }
}

[HarmonyPatch(typeof(NTopBar), "Initialize")]
public static class TopBarInitReflowPatch
{
    public static void Postfix(NTopBar __instance)
    {
        if (__instance.IsInsideTree()) Arm(__instance);
        else __instance.TreeEntered += () => Arm(__instance);
    }

    private static void Arm(NTopBar bar)
    {
        if (!GodotObject.IsInstanceValid(bar) || bar.HasMeta("sts2p_bar_loop")) return;
        bar.SetMeta("sts2p_bar_loop", true);
        Loop(bar);
    }

    private static void Loop(NTopBar bar)
    {
        if (!GodotObject.IsInstanceValid(bar)) return;
        if (!bar.IsInsideTree())
        {
            if (bar.HasMeta("sts2p_bar_loop")) bar.RemoveMeta("sts2p_bar_loop");
            return;
        }
        try { TopBarReflow.Apply(bar); }
        catch (System.Exception e) { PortraitMod.Log("topbar reflow ERR: " + e.Message); }
        bar.GetTree().CreateTimer(0.8).Timeout += () => Loop(bar);
    }

    public static void Defer(NTopBar bar, int _)
    {
        if (!GodotObject.IsInstanceValid(bar) || !bar.IsInsideTree()) return;
        if (!bar.HasMeta("sts2p_bar_loop")) { bar.SetMeta("sts2p_bar_loop", true); Loop(bar); return; }
        bar.GetTree().CreateTimer(0.08).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(bar) && bar.IsInsideTree())
                TopBarReflow.Apply(bar);
        };
    }
}

[HarmonyPatch(typeof(NTopBar), "MaxPotionsChanged")]
public static class TopBarMaxPotionsReflowPatch
{
    public static void Postfix(NTopBar __instance) => TopBarInitReflowPatch.Defer(__instance, 0);
}

[HarmonyPatch(typeof(NPotionContainer), "GrowPotionHolders")]
public static class PotionGrowReflowPatch
{
    public static void Postfix(NPotionContainer __instance)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var bar = TopBarReflow.FindTopBar(__instance);
        if (bar is not null) TopBarInitReflowPatch.Defer(bar, 0);
    }
}
