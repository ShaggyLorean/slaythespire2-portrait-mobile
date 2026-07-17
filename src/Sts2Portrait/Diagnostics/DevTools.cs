using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace Sts2Portrait.Diagnostics;

/// <summary>
/// Köprüyü tek sefer başlatır. Bilinen-çalışan bir bağlamdan (MenuLayout.Apply) çağrılır.
/// </summary>
public static class DevTools
{
    public static void Ensure()
    {
        if (NGame.Instance is null) return;
        if (Engine.GetMainLoop() is SceneTree tree)
            Bridge.Start(tree);
    }
}
