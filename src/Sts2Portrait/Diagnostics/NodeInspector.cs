using System.Text;
using Godot;

namespace Sts2Portrait.Diagnostics;

/// <summary>
/// Çalışma zamanı node ağacı dökümcüsü — sadece STATİK yardımcı.
/// (Mod DLL'inden gelen özel Node tiplerinin _Input/_Process callback'leri çağrılmadığından
/// node tabanlı kullanım terk edildi; döküm Bridge komutlarından tetiklenir.)
/// </summary>
public static class NodeInspector
{
    public static void DumpTree(Node root, string label, int maxDepth = 40)
    {
        var sb = new StringBuilder($"\n===== {label} =====\n");
        Walk(sb, root, 0, maxDepth);
        GD.Print(sb.ToString());
    }

    private static void Walk(StringBuilder sb, Node node, int depth, int maxDepth)
    {
        Describe(sb, node, depth);
        if (depth >= maxDepth) return;
        foreach (var child in node.GetChildren())
            Walk(sb, child, depth + 1, maxDepth);
    }

    private static void Describe(StringBuilder sb, Node node, int depth)
    {
        sb.Append(' ', depth * 2);
        sb.Append(node.Name).Append(" [").Append(node.GetType().Name).Append(']');
        switch (node)
        {
            case Control c:
                sb.Append($" pos={V(c.Position)} size={V(c.Size)} scale={V(c.Scale)}")
                  .Append($" gpos={V(c.GlobalPosition)}")
                  .Append($" anchors=({c.AnchorLeft:F2},{c.AnchorTop:F2},{c.AnchorRight:F2},{c.AnchorBottom:F2})")
                  .Append(c.Visible ? "" : " HIDDEN");
                break;
            case Node2D n2:
                sb.Append($" pos={V(n2.Position)} scale={V(n2.Scale)} gpos={V(n2.GlobalPosition)}")
                  .Append(n2.Visible ? "" : " HIDDEN");
                break;
        }
        sb.Append('\n');
    }

    private static string V(Vector2 v) => $"({v.X:F0},{v.Y:F0})";
}
