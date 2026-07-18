using Godot;
using HarmonyLib;

namespace Sts2Portrait.Patches;

public static class EventLayout
{
    public static float BlockWidth = 800f;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom), "_Ready")]
public static class EventTextCenterPatch
{
    public static void Postfix(object __instance) => Retry((Node)__instance, 0);

    private static void Retry(Node room, int attempt)
    {
        if (attempt > 12) return;
        room.GetTree().CreateTimer(0.3).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return;
            if (!Apply(room)) Retry(room, attempt + 1);
        };
    }

    private static bool Apply(Node room)
    {
        var canvas = PortraitConfig.CanvasSize;
        if (!PortraitConfig.IsPortrait(canvas)) return true;
        var title = ShopReflow.FindControl(room, "Title");
        if (title is null || title.GetParent() is not Control parent) return false;

        float w = title.Size.X > 1 ? title.Size.X : EventLayout.BlockWidth;
        float targetX = (canvas.X - w) / 2f;
        if (Mathf.Abs(parent.GlobalPosition.X - targetX) < 1f) return true;
        parent.GlobalPosition = new Vector2(targetX, parent.GlobalPosition.Y);
        PortraitMod.Log($"event text block centered -> x={targetX:F0} (w={w:F0})");
        return true;
    }
}
