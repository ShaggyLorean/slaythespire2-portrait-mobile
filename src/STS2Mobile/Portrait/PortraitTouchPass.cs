using System;
using Godot;

namespace STS2Mobile.Portrait;

// Fixing small buttons one screen at a time never converges: this game has
// dozens of screens, and our own fit-to-canvas scaling shrinks authored rows
// below a thumb target even where the original numbers were fine (the event
// options are authored 100 units tall and land at 83 after the layout fit).
//
// So the rule is enforced globally instead of per screen. Every patched screen
// runs this sweep from its own root: any clickable whose VISIBLE height is
// under the touch minimum is grown, and any block of clickables hanging into
// the gesture strip is lifted out of it.
//
// Deliberately conservative, because a blanket resize can wreck a layout:
//   - only clickables, identified by the signal the game's own button base
//     declares, never by guessing type names
//   - only growth, never shrink, and never past a sane multiple of the
//     authored size
//   - growth goes through CustomMinimumSize on container children, which is
//     the only channel that survives a container sort (a scale does not)
//   - anything a screen pass already places explicitly opts out by marker
internal static class PortraitTouchPass
{
    private const string ManagedMeta = "sts2_portrait_managed";
    private const string GrownMeta = "sts2_portrait_touch_grown";
    private const float MaxGrowthFactor = 2.2f;
    private const int NodeBudget = 6000;

    // Below this a control is an icon or a chip, not a list row.
    private const float MinimumRowWidth = 320f;

    private static readonly StringName ReleasedSignal = "Released";
    private static readonly System.Collections.Generic.HashSet<string> _reportedRoots = new();

    // A screen pass that places a node owns it, and so does everything under
    // that node: the global sweep growing a HUD row we just laid out is how a
    // blanket rule turns into a regression.
    private static bool IsManaged(Control control)
    {
        for (Node node = control; node is not null; node = node.GetParent())
        {
            if (node.HasMeta(ManagedMeta))
                return true;
        }

        return false;
    }

    // Screens opt a node out of the sweep when they place it themselves.
    internal static void MarkManaged(Control control) => control?.SetMeta(ManagedMeta, true);

    // One central sweep beats a per-screen one: a screen's patch is attached
    // wherever its hook happened to be, and the clickables it cares about
    // routinely live in a different subtree (the event options are not under
    // the event room node at all). Walking from the scene root needs to know
    // nothing about any screen's shape.
    private static bool _monitorStarted;

    internal static void StartMonitor(SceneTree tree)
    {
        if (_monitorStarted || tree?.Root is null || !OperatingSystem.IsAndroid())
            return;

        _monitorStarted = true;
        Tick(tree);
    }

    private static void Tick(SceneTree tree)
    {
        tree.CreateTimer(1.2).Timeout += () =>
        {
            try
            {
                if (tree.Root is { } root)
                    Apply(root);
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[Touch] sweep failed: {ex.GetBaseException().Message}");
            }

            Tick(tree);
        };
    }

    internal static void Apply(Node root)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (root is null || !PortraitDisplay.IsPortrait(canvas))
            return;

        var target = PortraitHudMetrics.MinTouchSide;
        var contentBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());
        var visited = 0;
        var grown = 0;
        var clickable = 0;
        var lowestBottom = 0f;
        Control lowestBlock = null;

        void Walk(Node node)
        {
            if (visited++ > NodeBudget)
                return;

            if (node is Control control && control.Visible && control.IsInsideTree() && control.IsVisibleInTree())
            {
                if (IsClickable(control) && !IsManaged(control))
                {
                    clickable++;
                    var scale = control.GetGlobalTransform().Scale;
                    var visibleHeight = control.Size.Y * scale.Y;

                    if (visibleHeight > 1f && visibleHeight < target && scale.Y > 0.05f)
                    {
                        if (Grow(control, target / scale.Y))
                        {
                            grown++;
                            PatchHelper.Log(
                                $"[Touch] grew '{control.Name}' {control.GetType().Name}"
                                + $" {control.Size.Y:F0}x{scale.Y:F2}={visibleHeight:F0} -> {target:F0}"
                            );
                        }
                    }

                    // Track the lowest clickable so a block that hangs into the
                    // gesture strip can be lifted as a whole.
                    var bottom = control.GetGlobalRect().Position.Y + control.Size.Y * scale.Y;
                    if (bottom > lowestBottom)
                    {
                        lowestBottom = bottom;
                        lowestBlock = control;
                    }
                }
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }

        Walk(root);

        if (lowestBlock is not null && lowestBottom > contentBottom)
            LiftOutOfGestureStrip(lowestBlock, lowestBottom - contentBottom);

        // Report every sweep once per root, not just the ones that changed
        // something: a sweep that finds no clickables at all is the interesting
        // case, and silence cannot tell the two apart.
        if (!_reportedRoots.Contains(root.Name))
        {
            _reportedRoots.Add(root.Name);
            PatchHelper.Log(
                $"[Touch] sweep '{root.Name}': visited={visited} clickable={clickable} grown={grown} lowest={lowestBottom:F0} limit={contentBottom:F0}"
            );
        }
        else if (grown > 0)
        {
            PatchHelper.Log($"[Touch] grew {grown} control(s) below {target:F0} units on {root.Name}");
        }
    }

    // NClickableControl declares "Released"; every button in this game inherits
    // it, and Godot's own BaseButton covers the rest. Checking the signal beats
    // a type-name list that would rot with every game update.
    private static bool IsClickable(Control control)
        => control is BaseButton || control.HasSignal(ReleasedSignal);

    private static bool Grow(Control control, float wantedHeight)
    {
        var authored = control.Size.Y;
        if (authored <= 1f || wantedHeight > authored * MaxGrowthFactor)
            return false;

        // The sweep only touches ONE shape: a full-width row in a vertical
        // list. That is the pattern the small-button complaints are actually
        // about - event choices, settings rows, menu entries, dialog options -
        // and it is the pattern where a short row is unambiguously a touch
        // defect rather than a deliberate design.
        //
        // Everything else is left alone on purpose. A horizontal strip of
        // icons (relics, potions, the top bar) is compact by intent, and
        // growing one of those is how a global rule becomes a regression: the
        // first version of this sweep inflated a relic holder and the potion
        // box before this shape test existed.
        if (control.GetParent() is not BoxContainer box || box.Vertical == false)
            return false;

        var visibleWidth = control.Size.X * control.GetGlobalTransform().Scale.X;
        if (visibleWidth < MinimumRowWidth)
            return false;

        if (control.CustomMinimumSize.Y >= wantedHeight - 0.5f)
            return false;

        control.CustomMinimumSize = new Vector2(control.CustomMinimumSize.X, wantedHeight);
        control.SetMeta(GrownMeta, true);
        return true;
    }

    // Lift the nearest ancestor that can actually be moved: a container child
    // would just be re-sorted back into place.
    private static void LiftOutOfGestureStrip(Control lowest, float overflow)
    {
        if (overflow <= 1f)
            return;

        for (Node node = lowest; node is Control control; node = control.GetParent())
        {
            if (control.GetParent() is Container)
                continue;

            if (control.HasMeta(ManagedMeta))
                return;

            control.Position -= new Vector2(0f, overflow);
            PatchHelper.Log(
                $"[Touch] lifted '{control.Name}' {overflow:F0} units out of the gesture strip"
            );
            return;
        }
    }
}
