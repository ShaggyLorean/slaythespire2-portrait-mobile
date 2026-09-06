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

        // Pure combat is fully owned by the combat pass (everything there is
        // MarkManaged) and this full-tree walk showed up as the periodic
        // 70-120ms frame spike that made card drags hitch. Skip it until an
        // overlay (loot, card pick, deck) is actually up.
        if (PortraitCombat.CombatHudActive && PortraitSceneCache.TopOverlay() is null)
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
        RaiseBodyTextFloor(root);

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
        GrowText(control, wantedHeight / authored);
        return true;
    }

    private static readonly StringName FontSizeName = "font_size";
    // Rich text draws each style run with its own themed size: raising only
    // normal_font_size left BBCode-bold headings tiny under their own grown
    // descriptions, flipping the hierarchy. The whole family moves together.
    private static readonly StringName[] RichFontSizeNames =
    {
        new StringName("normal_font_size"),
        new StringName("bold_font_size"),
        new StringName("italics_font_size"),
        new StringName("bold_italics_font_size"),
    };
    private static readonly StringName AuthoredFontMeta = "sts2_touch_authored_font";

    // A row grown without its text is worse than a small row: a huge plate
    // with a tiny caption in the corner reads as broken. Whatever factor the
    // row grew by, its labels grow with it, through font-size overrides so the
    // game's own hover animations (which write label Scale) stay untouched.
    // The authored size is remembered in metadata so repeat sweeps cannot
    // compound the growth.
    private static void GrowText(Node root, float factor)
    {
        if (factor <= 1.01f)
            return;

        void Walk(Node node)
        {
            switch (node)
            {
                case Label label:
                    ApplyFontOverride(label, FontSizeName, factor);
                    break;
                case RichTextLabel rich:
                    foreach (var sizeName in RichFontSizeNames)
                        ApplyFontOverride(rich, sizeName, factor);
                    break;
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }

        Walk(root);
    }

    // Long-form text has its own floor. 46 canvas units is 14dp on the
    // reference device - the size below which story text stops being
    // comfortable to read at arm's length. Only wrapped or long text
    // qualifies: short labels are counters and badges, and inflating those
    // wrecks the HUD (the first draft of this rule did exactly that to the
    // potion count).
    private const float BodyTextFloor = 46f;
    private const int LongTextThreshold = 60;
    private static int _textRaised;

    private static void RaiseBodyTextFloor(Node root)
    {
        var raised = 0;

        void Walk(Node node)
        {
            if (node is Control { Visible: true } control && control.IsVisibleInTree() && !IsManaged(control)
                && !IsInsideCardFace(control))
            {
                var scaleY = control.GetGlobalTransform().Scale.Y;
                if (scaleY > 0.05f)
                {
                    switch (control)
                    {
                        case Label label when label.AutowrapMode != TextServer.AutowrapMode.Off
                            || (label.Text?.Length ?? 0) >= LongTextThreshold:
                            raised += RaiseFloor(label, FontSizeName, scaleY) ? 1 : 0;
                            break;
                        case RichTextLabel rich when (rich.Text?.Length ?? 0) >= LongTextThreshold:
                        {
                            var any = false;
                            foreach (var sizeName in RichFontSizeNames)
                                any |= RaiseFloor(rich, sizeName, scaleY);
                            raised += any ? 1 : 0;
                            break;
                        }
                    }
                }
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }

        Walk(root);
        if (raised > 0 && _textRaised != raised)
        {
            _textRaised = raised;
            PatchHelper.Log($"[Touch] raised body text floor on {raised} label(s)");
        }
    }

    // Card faces size their text with the game's own autosize-to-rect pass;
    // a floor forced on top of that clips long descriptions while short ones
    // stay small - the single most visible inconsistency this sweep ever
    // produced. Anything card-shaped owns its own typography.
    private static bool IsInsideCardFace(Node node)
    {
        for (var current = node; current is not null; current = current.GetParent())
        {
            var typeName = current.GetType().Name;
            if (typeName.StartsWith("NCard", StringComparison.Ordinal)
                || typeName.StartsWith("NTinyCard", StringComparison.Ordinal)
                || typeName is "NUpgradePreview" or "NEnchantPreview" or "NTransformPreview")
                return true;
        }

        return false;
    }

    private static bool RaiseFloor(Control label, StringName sizeName, float scaleY)
    {
        var current = label.GetThemeFontSize(sizeName);
        if (current <= 0 || current * scaleY >= BodyTextFloor)
            return false;

        var metaKey = AuthoredFontMeta + "_" + sizeName;
        if (label.GetMeta(metaKey, Variant.From(0)).AsInt32() <= 0)
        {
            label.SetMeta(metaKey, current);
        }

        var target = Mathf.RoundToInt(BodyTextFloor / scaleY);
        if (label.HasThemeFontSizeOverride(sizeName) && label.GetThemeFontSize(sizeName) == target)
            return false;

        label.AddThemeFontSizeOverride(sizeName, target);
        return true;
    }

    private static void ApplyFontOverride(Control label, StringName sizeName, float factor)
    {
        var metaKey = AuthoredFontMeta + "_" + sizeName;
        var authored = label.GetMeta(metaKey, Variant.From(0)).AsInt32();
        if (authored <= 0)
        {
            authored = label.GetThemeFontSize(sizeName);
            if (authored <= 0)
                return;

            label.SetMeta(metaKey, authored);
        }

        // Inside a grown row every label reaches at least the body floor:
        // otherwise the row's short title ends up SMALLER than its long
        // description (the body floor catches only long text) and the type
        // hierarchy flips upside down.
        var scaleY = label.GetGlobalTransform().Scale.Y;
        var target = Mathf.RoundToInt(authored * factor);
        if (scaleY > 0.05f)
            target = Math.Max(target, Mathf.RoundToInt(BodyTextFloor / scaleY));

        if (label.HasThemeFontSizeOverride(sizeName) && label.GetThemeFontSize(sizeName) == target)
            return;

        label.AddThemeFontSizeOverride(sizeName, target);
    }

    // Lift the nearest ancestor that can actually be moved: a container child
    // would just be re-sorted back into place.
    private static void LiftOutOfGestureStrip(Control lowest, float overflow)
    {
        if (overflow <= 1f)
            return;

        // Scrollable content (relic collection, patch notes, ...) reaches the
        // strip by design and its NScrollableContainer drives the position
        // every frame; lifting it made the list bounce up and down.
        for (Node probe = lowest; probe is not null; probe = probe.GetParent())
            if (probe.GetType().Name == "NScrollableContainer")
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
