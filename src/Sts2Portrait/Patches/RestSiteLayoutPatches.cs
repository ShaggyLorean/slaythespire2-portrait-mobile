using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

/// <summary>
/// Dinlenme (campfire) odası: BgContainer içindeki sahne sanatı (ör. RestSiteBG 2765×1296)
/// dikeyde canvas'ı kaplamıyor → üstte siyah bant. Sanatın GLOBAL MERKEZİNİ sabit tutarak
/// container'ı cover ölçeğine büyüt (kamp ateşi/karakter ayrı düzlemde, etkilenmez).
/// Act'e göre alt-node adı değişir (HiveRestSite vb.) → sanat = en büyük TextureRect.
/// </summary>
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
            if (bg is null || bg.HasMeta("sts2p_cover")) return;   // çifte ölçek koruması
            var art = LargestTexture(bg);
            if (art is null) return;

            float k = RestSiteLayout.BgScale;
            Vector2 center = art.GetGlobalRect().GetCenter();
            bg.SetMeta("sts2p_cover", true);
            bg.Scale = Vector2.One * k;
            // Sanat merkezi sabit kalacak şekilde ölçekle: x' = c + k(x - c)
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
