using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

/// <summary>
/// Savaş ekranı dikey yerleşimi.
/// - NCombatSceneContainer: bg dikeyde 0.9 ölçekle kalıyor (üstte siyah bant) → cover ölçek.
/// - NPlayerHand: el kartları canvas dibinde kesiliyor → CardHolderContainer'ı yukarı al.
/// </summary>
public static class CombatLayout
{
    public static float BgScale = 1.7f;              // savaş arka planı dikeyi kaplasın (2765x1296 art)
    public static float HandScale = 0.70f;           // fanı küçült → 10 kart 1080 genişliğe sığar
    public static float FanBottomFromCanvas = 400f;  // fan origin = CanvasSize.Y - bu (~1760)
    public static float CtrlUpFromBottom = 180f;     // kontroller CanvasSize.Y - bu (~1980)
    public static float EnergyXFrac = 0.084f;        // enerji sol kenar ~95px
    public static float EndTurnRightInset = 150f;    // end-turn sağ kenardan içeri
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
        // _Ready'de _bgContainer henüz atanmış olmayabilir; kısa gecikmeyle uygula.
        var node = (Node)__instance;
        node.GetTree().CreateTimer(0.1).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                CombatBgWindowChangePatch.ScaleBg(__instance);
        };
    }
}

// El fanı: kör 170px kaldırma yerine, fanı KÜÇÜLT (0.70) → 10 karta kadar hep ekranda kalır,
// ve origin'i MUTLAK bir Y'ye al. Fanın alt kenarı hand size'dan bağımsız sabitlenir.
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
            var c = Traverse.Create(__instance).Property("CardHolderContainer").GetValue<Control>();
            if (c is null) return;
            c.Scale = Vector2.One * CombatLayout.HandScale;
            float targetOriginY = canvas.Y - CombatLayout.FanBottomFromCanvas;
            c.Position = new Vector2(c.Position.X, targetOriginY);
            PortraitMod.Log($"hand scaled {CombatLayout.HandScale}, originY={targetOriginY}");
        };
    }
}

// Kalıcı kontrolleri (enerji) fanın DAİMA altında kalan bir şeride taşı.
// NCombatUi.Activate'ten SONRA çalışır (Regent'in (100,806) override'ından ve enerji AddChild'ından sonra).
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi), "Activate")]
public static class CombatControlsPatch
{
    public static void Postfix(object __instance)
    {
        var ui = (Node)__instance;
        ui.GetTree().CreateTimer(0.05).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(ui) || !ui.IsInsideTree()) return;
            var canvas = PortraitConfig.CanvasSize;
            if (!PortraitConfig.IsPortrait(canvas)) return;
            var energy = Traverse.Create(__instance).Property("EnergyCounterContainer").GetValue<Control>();
            if (energy is not null)
            {
                energy.AnchorLeft = energy.AnchorTop = energy.AnchorRight = energy.AnchorBottom = 0f;
                energy.Position = new Vector2(canvas.X * CombatLayout.EnergyXFrac, canvas.Y - CombatLayout.CtrlUpFromBottom);
                PortraitMod.Log($"energy -> {energy.Position}");
            }
        };
    }
}

// End-turn butonu her AnimIn'de ShowPos'a yeniden tween'lenir; tween SONRASI sağ-alt şeride sabitle.
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
            if (GodotObject.IsInstanceValid(btn) && btn.IsInsideTree())
                btn.Position = new Vector2(canvas.X - CombatLayout.EndTurnRightInset, canvas.Y - CombatLayout.CtrlUpFromBottom);
        };
    }
}
