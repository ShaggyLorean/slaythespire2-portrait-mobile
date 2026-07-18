using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

public static class CombatLayout
{
    public static float BgScale = 1.7f;
    public static float HandScale = 0.82f;
    public static float FanXCompress = 0.72f;
    // Everything below is a FRACTION of the live canvas, so it adapts to any phone size.
    public static float FanBottomFrac = 0.155f;   // hand fan origin, from bottom
    public static float CtrlUpFrac = 0.072f;       // energy / end-turn strip, from bottom
    public static float EnergyXFrac = 0.084f;      // energy left inset
    public static float EndTurnRightFrac = 0.02f;  // end-turn right margin (button pinned to right edge)
    public static float TargetDockYFrac = 0.72f;   // held targeted card docks here (near the finger)
    public static float TargetDockScale = 0.62f;
    public static float EnergyUpFrac = 0.145f;     // energy sits higher so it clears the draw pile
    public static float AllyCenterXFrac = 0.545f;  // creature rows nudged inward so nothing hugs the edges
    public static float EnemyCenterXFrac = 0.385f;
}

// When a targeted card is picked up, the game docks it at the bottom center of the
// canvas. On a tall portrait canvas that is far below the hand, so on touch the card
// looks like it teleports away from the finger and the aim arrow starts from the
// bottom. Re-dock it just above the hand, close to where the finger already is.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay), "CenterCard")]
public static class TargetDockPatch
{
    public static void Postfix(object __instance)
    {
        var canvas = PortraitConfig.CanvasSize;
        if (!PortraitConfig.IsPortrait(canvas)) return;
        Tune.Reload();
        var holder = Traverse.Create(__instance).Property("Holder").GetValue();
        if (holder is null) return;
        var t = Traverse.Create(holder);
        t.Method("SetTargetPosition", new Vector2(canvas.X / 2f, canvas.Y * CombatLayout.TargetDockYFrac)).GetValue();
        t.Method("SetTargetScale", Vector2.One * CombatLayout.TargetDockScale).GetValue();
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Helpers.HandPosHelper), "GetPosition")]
public static class HandFanCompressPatch
{
    public static void Postfix(ref Vector2 __result)
    {
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        __result.X *= CombatLayout.FanXCompress;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCombatSceneContainer), "OnWindowChange")]
public static class CombatBgWindowChangePatch
{
    public static void Postfix(object __instance)
    {
        ScaleBg(__instance);
    }

    public static void ScaleBg(object instance)
    {
        var node = (Node)instance;
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var bg = Traverse.Create(instance).Field("_bgContainer").GetValue<Control>();
        if (bg is null) return;
        bg.Scale = Vector2.One * CombatLayout.BgScale;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCombatSceneContainer), "_Ready")]
public static class CombatBgReadyPatch
{
    public static void Postfix(object __instance)
    {
        var node = (Node)__instance;
        node.GetTree().CreateTimer(0.1).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                CombatBgWindowChangePatch.ScaleBg(__instance);
        };
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand), "_Ready")]
public static class HandRaisePatch
{
    public static void Postfix(object __instance)
    {
        var hand = (Node)__instance;
        hand.GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree()) return;
            var canvas = PortraitConfig.CanvasSize;
            if (!PortraitConfig.IsPortrait(canvas)) return;
            Tune.Reload();
            var c = Traverse.Create(__instance).Property("CardHolderContainer").GetValue<Control>();
            if (c is null) return;
            c.Scale = Vector2.One * CombatLayout.HandScale;
            float targetOriginY = canvas.Y * (1f - CombatLayout.FanBottomFrac);
            c.Position = new Vector2(c.Position.X, targetOriginY);
            PortraitMod.Log($"hand scaled {CombatLayout.HandScale}, originY={targetOriginY}");
        };
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi), "Activate")]
public static class CombatControlsPatch
{
    public static void Postfix(object __instance)
    {
        var ui = (Node)__instance;
        foreach (var delay in new[] { 0.05, 0.6, 1.6, 3.2 })
        {
            ui.GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(ui) || !ui.IsInsideTree()) return;
                var canvas = PortraitConfig.CanvasSize;
                if (!PortraitConfig.IsPortrait(canvas)) return;
                Tune.Reload();
                var energy = Traverse.Create(__instance).Property("EnergyCounterContainer").GetValue<Control>();
                if (energy is not null)
                {
                    energy.AnchorLeft = energy.AnchorTop = energy.AnchorRight = energy.AnchorBottom = 0f;
                    energy.Position = new Vector2(canvas.X * CombatLayout.EnergyXFrac, canvas.Y * (1f - CombatLayout.EnergyUpFrac));
                }
                NudgeCreatureRows(ui, canvas);
            };
        }
    }

    // The creature rows are centered for a wide screen, which pins the player's health
    // bar to the left edge and the last enemy to the right edge on a narrow canvas.
    // Pull both containers toward the middle.
    private static void NudgeCreatureRows(Node ui, Vector2 canvas)
    {
        var scene = ui.GetTree().Root;
        var ally = ShopReflow.FindControl(scene, "AllyContainer");
        var enemy = ShopReflow.FindControl(scene, "EnemyContainer");
        if (ally is not null)
            ally.Position = new Vector2(canvas.X * CombatLayout.AllyCenterXFrac, ally.Position.Y);
        if (enemy is not null)
            enemy.Position = new Vector2(canvas.X * CombatLayout.EnemyCenterXFrac, enemy.Position.Y);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "AnimIn")]
public static class EndTurnBottomPatch
{
    public static void Postfix(object __instance)
    {
        var btn = (Control)__instance;
        var canvas = PortraitConfig.CanvasSize;
        if (!PortraitConfig.IsPortrait(canvas)) return;
        btn.GetTree().CreateTimer(0.55).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(btn) || !btn.IsInsideTree()) return;
            Tune.Reload();
            float w = btn.Size.X > 1 ? btn.Size.X : 240f;
            float x = canvas.X - canvas.X * CombatLayout.EndTurnRightFrac - w;   // pin right edge inside canvas
            btn.Position = new Vector2(x, canvas.Y * (1f - CombatLayout.CtrlUpFrac));
        };
    }
}
