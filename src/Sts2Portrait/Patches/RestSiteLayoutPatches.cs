using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

public static class RestSiteLayout
{
    public static float BgScale = 1.7f;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom), "_Ready")]
public static class RestSiteBgCoverPatch
{
    public static void Postfix(object __instance)
    {
        var room = (Node)__instance;
        room.GetTree().CreateTimer(0.2).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return;
            if (!PortraitConfig.IsPortrait(PortraitConfig.CanvasSize)) return;
            var bg = ShopReflow.FindControl(room, "BgContainer");
            if (bg is null || bg.HasMeta("sts2p_cover")) return;
            var art = LargestTexture(bg);
            if (art is null) return;

            float k = RestSiteLayout.BgScale;
            Vector2 center = art.GetGlobalRect().GetCenter();
            bg.SetMeta("sts2p_cover", true);
            bg.Scale = Vector2.One * k;
            bg.Position = center * (1f - k) + bg.Position * k;
            PortraitMod.Log($"restsite bg cover x{k} (art center {center})");
        };
    }

    private static Control? LargestTexture(Node root)
    {
        Control? best = null;
        float bestArea = 0f;
        Walk(root);
        return best;

        void Walk(Node n)
        {
            if (n is TextureRect tr)
            {
                float area = tr.Size.X * tr.Size.Y;
                if (area > bestArea) { bestArea = area; best = tr; }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }
}
