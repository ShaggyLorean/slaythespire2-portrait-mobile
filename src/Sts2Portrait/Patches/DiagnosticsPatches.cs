using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using Sts2Portrait.Diagnostics;

namespace Sts2Portrait.Patches;

[HarmonyPatch(typeof(NSceneContainer), "SetCurrentScene")]
public static class SceneContainerDumpPatch
{
    public static void Postfix(NSceneContainer __instance, Control node)
    {
        if (node is null)
            return;
        node.GetTree().CreateTimer(0.1).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                NodeInspector.DumpTree(node, $"SCENE: {node.Name} [{node.GetType().Name}]");
        };
    }
}
