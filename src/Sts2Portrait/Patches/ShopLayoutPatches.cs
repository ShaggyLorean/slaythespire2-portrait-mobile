using System.Collections.Generic;
using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

public static class ShopLayout
{
    public static float PanelMarginX = 20f;
    public static float PanelTop = 220f;
    public static float PanelBottom = 170f;
    public static int   Cols = 3;
    public static float GridTop = 26f;
    public static float CellH = 515f;
    public static float CardScale = 1.0f;
    public static float BandH = 210f;
    public static float TrinketScale = 1.0f;
    public static float RelicBandX = 215f;
    public static float PotionBandXFrac = 0.60f;
    public static float BandYOffset = -14f;
    public static float BgCoverScale = 1.75f;
}

public static class ShopReflow
{
    private static string _lastLog = "";

    public static void Apply(Node inv)
    {
        Tune.Reload();
        if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
        var slots = FindControl(inv, "SlotsContainer");
        if (slots is null) return;

        var canvas = PortraitConfig.CanvasSize;
        float panelW = canvas.X - 2 * ShopLayout.PanelMarginX;
        float panelH = canvas.Y - ShopLayout.PanelTop - ShopLayout.PanelBottom;

        if (slots is TextureRect tr)
        {
            tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            tr.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
        slots.AnchorLeft = slots.AnchorTop = slots.AnchorRight = slots.AnchorBottom = 0f;
        slots.PivotOffset = Vector2.Zero;
        slots.Scale = Vector2.One;
        slots.Position = new Vector2(ShopLayout.PanelMarginX, ShopLayout.PanelTop);
        slots.Size = new Vector2(panelW, panelH);

        var cards = new List<Control>();
        CollectByType(slots, "NMerchantCard", cards);
        cards.RemoveAll(c => !c.Visible);
        var removal = FindFirstByType(slots, "NMerchantCardRemoval");
        var relics = FindControl(slots, "Relics");
        var potions = FindControl(slots, "Potions");

        bool hasRemoval = removal is { Visible: true };
        int items = cards.Count + (hasRemoval ? 1 : 0);
        int rows = Mathf.Max(1, (items + ShopLayout.Cols - 1) / ShopLayout.Cols);
        float availH = panelH - ShopLayout.BandH - ShopLayout.GridTop;
        float fit = Mathf.Min(1f, availH / (rows * ShopLayout.CellH));
        float cellW = panelW / ShopLayout.Cols;
        float cellH = ShopLayout.CellH * fit;

        var origin = slots.GlobalPosition;
        for (int i = 0; i < cards.Count; i++)
            PlaceOrigin(cards[i], origin + CellCenter(i, cellW, cellH), ShopLayout.CardScale * fit);
        if (hasRemoval && removal is not null)
            PlaceOrigin(removal, origin + CellCenter(cards.Count, cellW, cellH), fit);

        float iconH = 122f * ShopLayout.TrinketScale;
        float bandY = panelH - ShopLayout.BandH + (ShopLayout.BandH - iconH) * 0.5f + ShopLayout.BandYOffset;
        if (relics is not null)
            PlaceOrigin(relics, origin + new Vector2(ShopLayout.RelicBandX, bandY), ShopLayout.TrinketScale);
        if (potions is not null)
            PlaceOrigin(potions, origin + new Vector2(panelW * ShopLayout.PotionBandXFrac, bandY), ShopLayout.TrinketScale);

        var back = FindControl(inv, "BackButton");
        if (back is not null)
            back.Position = new Vector2(-40f, canvas.Y - 354f);

        var log = $"shop reflow: items={items} rows={rows} fit={fit:F2} panel={panelW:F0}x{panelH:F0}";
        if (log != _lastLog) { _lastLog = log; PortraitMod.Log(log); }
    }

    private static Vector2 CellCenter(int i, float cellW, float cellH)
    {
        int r = i / ShopLayout.Cols, c = i % ShopLayout.Cols;
        return new Vector2(c * cellW + cellW / 2f, ShopLayout.GridTop + r * cellH + cellH / 2f);
    }

    private static void PlaceOrigin(Control n, Vector2 targetGlobal, float scale)
    {
        n.PivotOffset = Vector2.Zero;
        n.Scale = Vector2.One * scale;
        n.Position += targetGlobal - n.GlobalPosition;
    }

    internal static Control? FindControl(Node root, string name)
    {
        if (root is Control c && root.Name == name) return c;
        foreach (var child in root.GetChildren())
        {
            var r = FindControl(child, name);
            if (r is not null) return r;
        }
        return null;
    }

    private static void CollectByType(Node root, string typeName, List<Control> into)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control c && child.GetType().Name == typeName) into.Add(c);
            else CollectByType(child, typeName, into);
        }
    }

    private static Control? FindFirstByType(Node root, string typeName)
    {
        if (root is Control c && root.GetType().Name == typeName) return c;
        foreach (var child in root.GetChildren())
        {
            var r = FindFirstByType(child, typeName);
            if (r is not null) return r;
        }
        return null;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory), "Open")]
public static class MerchantOpenReflowPatch
{
    public static void Postfix(object __instance)
    {
        var inv = (Node)__instance;
        if (inv.HasMeta("sts2p_shop_closed")) inv.RemoveMeta("sts2p_shop_closed");
        if (inv.HasMeta("sts2p_shop_loop")) return;
        inv.SetMeta("sts2p_shop_loop", true);
        Loop(inv);
    }

    private static void Loop(Node inv)
    {
        if (!GodotObject.IsInstanceValid(inv)) return;
        if (!inv.IsInsideTree())
        {
            if (inv.HasMeta("sts2p_shop_loop")) inv.RemoveMeta("sts2p_shop_loop");
            return;
        }
        bool closed = inv.HasMeta("sts2p_shop_closed") ||
                      (inv is CanvasItem ci && !ci.IsVisibleInTree());
        if (!closed)
        {
            var isOpen = Traverse.Create(inv).Property("IsOpen");
            if (isOpen.PropertyExists()) closed = !isOpen.GetValue<bool>();
        }
        if (!closed)
        {
            try { ShopReflow.Apply(inv); }
            catch (System.Exception e) { PortraitMod.Log("shop reflow ERR: " + e.Message); }
        }
        inv.GetTree().CreateTimer(0.4).Timeout += () => Loop(inv);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory), "Close")]
public static class MerchantCloseReflowPatch
{
    public static void Prefix(object __instance) => ((Node)__instance).SetMeta("sts2p_shop_closed", true);
}

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
            var bg = ShopReflow.FindControl(room, "BgContainer");
            if (bg is null) return;
            bg.PivotOffset = bg.Size / 2f;
            bg.Scale = Vector2.One * ShopLayout.BgCoverScale;
            PortraitMod.Log($"shop bg cover x{ShopLayout.BgCoverScale}");
        };
    }
}
