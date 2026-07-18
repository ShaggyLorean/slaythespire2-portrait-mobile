using Godot;

namespace Sts2Portrait;

public static class StatusStripHider
{
    private static bool _started;
    private static Control? _strip;
    private static int _misses;

    public static void Start()
    {
        if (_started) return;
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _started = true;
        Loop(tree);
    }

    private static void Loop(SceneTree tree)
    {
        double delay = 2.0;
        try
        {
            if (_strip is not null && GodotObject.IsInstanceValid(_strip))
            {
                if (_strip.Visible) _strip.Visible = false;
                _misses = 0;
            }
            else
            {
                _strip = null;
                var label = FindLabel(tree.Root);
                if (label is not null)
                {
                    Control target = label;
                    for (Node? p = label.GetParent(); p is Control c; p = p.GetParent())
                    {
                        if (c.Size.Y > 160f) break;
                        target = c;
                    }
                    target.Visible = false;
                    _strip = target;
                    _misses = 0;
                    PortraitMod.Log($"mod status strip hidden: {target.Name} {target.Size}");
                }
                else if (++_misses > 5)
                {
                    delay = 10.0;
                }
            }
        }
        catch { }
        tree.CreateTimer(delay).Timeout += () => Loop(tree);
    }

    private static Control? FindLabel(Node n)
    {
        string? t = n switch { Label l => l.Text, RichTextLabel r => r.Text, _ => null };
        if (t is not null && (t.Contains("Running Modded") || (t.Contains("Loaded") && t.Contains("mod"))))
            return (Control)n;
        foreach (var c in n.GetChildren())
        {
            var r = FindLabel(c);
            if (r is not null) return r;
        }
        return null;
    }
}
