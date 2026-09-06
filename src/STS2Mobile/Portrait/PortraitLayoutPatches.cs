using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Saves;
using STS2Mobile.Patches;

namespace STS2Mobile.Portrait;


// Every layout system here used to answer "what is on screen right now?" by
// walking the whole scene tree through the C# interop, several times per
// guard tick. At ~3700 nodes each walk allocates thousands of interop arrays
// and the ticks showed up as 130ms frame spikes on device: jank and heat.
// The game already maintains the answer; ask its overlay stack once and cache
// resolved controls until they leave the tree.
internal static class PortraitSceneCache
{
    private static Type _stackType;
    private static System.Reflection.PropertyInfo _stackInstance;
    private static System.Reflection.MethodInfo _stackPeek;
    private static readonly System.Collections.Generic.Dictionary<string, WeakReference<Control>> Controls = new();

    internal static Control TopOverlay()
    {
        try
        {
            _stackType ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack");
            if (_stackType is null)
                return null;
            _stackInstance ??= _stackType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            _stackPeek ??= _stackType.GetMethod("Peek", BindingFlags.Public | BindingFlags.Instance);
            var instance = _stackInstance?.GetValue(null);
            if (instance is null)
                return null;
            return _stackPeek?.Invoke(instance, null) as Control;
        }
        catch
        {
            return null;
        }
    }

    // Cached lookups: the full-tree walk happens only when the cached node
    // has died, which in practice is once per scene change. cacheKey scopes
    // generic names ("Map", "Pause") to their owner so screens cannot steal
    // each other's cache slots.
    internal static Control Find(Node root, string name, string cacheKey = null)
        => Resolve(cacheKey ?? name, () => PortraitNodes.FindControl(root, name));

    internal static Control FindByType(Node root, string typeName, string cacheKey = null)
        => Resolve(cacheKey ?? ("type:" + typeName), () => PortraitNodes.FindByType(root, typeName));

    // The pause chip is named "PauseButton" on some screens and "Pause" on
    // others; one scoped slot covers both spellings.
    internal static Control Resolve2(Node bar)
        => Resolve("topbar:pause", () => PortraitNodes.FindControl(bar, "PauseButton", "Pause"));

    private static Control Resolve(string key, Func<Control> finder)
    {
        if (Controls.TryGetValue(key, out var slot)
            && slot.TryGetTarget(out var cached)
            && GodotObject.IsInstanceValid(cached)
            && cached.IsInsideTree())
            return cached;

        var found = finder();
        if (found is not null)
            Controls[key] = new WeakReference<Control>(found);
        else
            Controls.Remove(key);
        return found;
    }
}

internal static class PortraitNodes
{
    internal static Control FindControl(Node root, params string[] names)
    {
        if (root is Control current)
        {
            foreach (var name in names)
            {
                if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase))
                    return current;
            }
        }

        foreach (var child in root.GetChildren())
        {
            var found = FindControl(child, names);
            if (found is not null)
                return found;
        }

        return null;
    }

    internal static Control FindByType(Node root, string typeName)
    {
        if (root is Control current && current.GetType().Name == typeName)
            return current;

        foreach (var child in root.GetChildren())
        {
            var found = FindByType(child, typeName);
            if (found is not null)
                return found;
        }

        return null;
    }

    internal static void CollectByType(Node root, string typeName, List<Control> destination)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control current && current.GetType().Name == typeName)
                destination.Add(current);
            else
                CollectByType(child, typeName, destination);
        }
    }

    internal static void ClearAnchors(Control control)
    {
        control.AnchorLeft = 0f;
        control.AnchorTop = 0f;
        control.AnchorRight = 0f;
        control.AnchorBottom = 0f;
    }

    // A thin rounded panel keeps peeking in from the left edge of every scene
    // on device and nobody knows which node it is. Log every visible control
    // that pokes into the left strip so the next trace names the culprit.
    // NOT /data/local/tmp: SELinux hides that directory from the .NET side
    // on this device even though the Java boot path can read it, which made
    // every managed file gate there silently dead.
    internal static string DumpTrigger
        => System.IO.Path.Combine(Godot.OS.GetUserDataDir(), "sts2_dump");

    // Field truth on demand: write "x,y,w,h" into the trigger file on the
    // device and the next pass logs every visible control overlapping that
    // region, top-down, with z and path. Answers "what is drawing here?"
    // without another build-deploy-refight cycle.
    internal static void DumpRegionOnRequest(Control screen)
    {
        string text;
        try
        {
            if (!System.IO.File.Exists(DumpTrigger))
                return;

            text = System.IO.File.ReadAllText(DumpTrigger).Trim();
            System.IO.File.Delete(DumpTrigger);
        }
        catch
        {
            return;
        }

        var region = new Rect2(830f, 1500f, 350f, 500f);
        var parts = text.Split(',');
        if (parts.Length == 4
            && float.TryParse(parts[0], out var x)
            && float.TryParse(parts[1], out var y)
            && float.TryParse(parts[2], out var w)
            && float.TryParse(parts[3], out var h))
            region = new Rect2(x, y, w, h);

        if (screen.GetTree()?.Root is not { } root)
            return;

        PatchHelper.Log($"[Portrait] region dump {region.Position.X:F0},{region.Position.Y:F0} {region.Size.X:F0}x{region.Size.Y:F0}:");
        var logged = 0;
        void Walk(Node node)
        {
            if (logged >= 90)
                return;
            // Visible alone lies: a node under a hidden parent still reports
            // Visible=true, which buried the real answer in dozens of controls
            // from screens that were not on screen at all.
            if (node is Control { Visible: true } control
                && control.IsInsideTree()
                && control.IsVisibleInTree())
            {
                var rect = control.GetGlobalRect();
                if (rect.Intersects(region))
                {
                    PatchHelper.Log(
                        $"[Portrait]   {control.GetType().Name} '{control.Name}' z={control.ZIndex} rel={control.ZAsRelative} a={control.Modulate.A:F2}/{control.SelfModulate.A:F2} rect={rect.Position.X:F0},{rect.Position.Y:F0} {rect.Size.X:F0}x{rect.Size.Y:F0} path={control.GetPath()}"
                    );
                    logged++;
                }
            }
            // Controls are not the whole story: a Node2D (a Spine sprite, a
            // hand-drawn panel, anything the game draws outside the Control
            // tree) has no rect to intersect, so report it by origin whenever
            // that origin sits inside the region or just off its left edge.
            // This is what names the panels that peek in from a screen edge.
            if (node is Node2D node2d && node2d.IsVisibleInTree())
            {
                var origin = node2d.GetGlobalTransform().Origin;
                if (origin.X > region.Position.X - 900f
                    && origin.X < region.End.X + 200f
                    && origin.Y > region.Position.Y - 500f
                    && origin.Y < region.End.Y + 500f)
                {
                    PatchHelper.Log(
                        $"[Portrait]   2D {node2d.GetType().Name} '{node2d.Name}' z={node2d.ZIndex} origin={origin.X:F0},{origin.Y:F0} path={node2d.GetPath()}"
                    );
                    logged++;
                }
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }
        Walk(root);
        PatchHelper.Log($"[Portrait] region dump complete ({logged})");
    }

    internal static void LogEdgePeekers(Node sceneRoot, Vector2 canvas)
    {
        var found = 0;
        void Walk(Node node)
        {
            if (found >= 12)
                return;

            if (node is Control control && control.Visible && control.IsInsideTree())
            {
                var rect = control.GetGlobalRect();
                var path = control.GetPath().ToString();
                // Parked back/unready buttons overhang the edge by ~40 units on
                // every screen; they are a known, separate nuisance. Skip them
                // so the cap is spent on the unknown peeker.
                if (rect.Position.X < 4f
                    && rect.End.X > 2f
                    && rect.End.X < 60f
                    && rect.Size.Y > 80f
                    && rect.Size.Y < canvas.Y * 0.6f
                    && !path.Contains("BackButton")
                    && !path.Contains("UnreadyButton"))
                {
                    PatchHelper.Log(
                        $"[Portrait] edge peek: {control.GetType().Name} '{control.Name}' rect={rect.Position.X:F0},{rect.Position.Y:F0} {rect.Size.X:F0}x{rect.Size.Y:F0} path={path}"
                    );
                    found++;
                }
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }

        Walk(sceneRoot);
        if (found == 0)
            PatchHelper.Log("[Portrait] edge peek: none found");
    }

    // NBackButton tweens global_position to its private _showPos on every
    // show, so a Position write is undone within the frame. Rewrite the
    // destination itself and the game's own slide lands the tab where the
    // portrait layout wants it (same source-hook idea as the piles).
    internal static void DumpSubtree(Node root, string tag, int maxDepth = 6)
    {
        void Walk(Node node, int depth)
        {
            if (depth > maxDepth)
                return;
            if (node is Control c)
            {
                var text = node switch
                {
                    RichTextLabel r => r.Text,
                    Label l => l.Text,
                    _ => "",
                };
                if (text.Length > 40)
                    text = text[..40];
                var gr = c.GetGlobalRect();
                PatchHelper.Log($"[Portrait] {tag} {new string(' ', depth * 2)}{c.Name}:{c.GetType().Name} pos={c.Position} size={c.Size} scale={c.Scale} global={gr.Position.X:F0},{gr.Position.Y:F0} {gr.Size.X:F0}x{gr.Size.Y:F0} vis={c.Visible} clip={c.ClipContents} text={text.Replace('\n', '|')}");
            }
            else if (node is Node2D n2)
            {
                var tex = node is Sprite2D sp && sp.Texture is not null ? $" tex={sp.Texture.GetWidth()}x{sp.Texture.GetHeight()} centered={sp.Centered} offset={sp.Offset}" : "";
                PatchHelper.Log($"[Portrait] {tag} {new string(' ', depth * 2)}{n2.Name}:{n2.GetType().Name} 2D pos={n2.Position} scale={n2.Scale} vis={n2.Visible}{tex}");
            }
            foreach (var child in node.GetChildren())
                Walk(child, depth + 1);
        }
        Walk(root, 0);
    }

    internal static void PlaceBackTab(Control back, Vector2 globalTarget)
    {
        if (back is null)
            return;
        try
        {
            Traverse.Create(back).Field("_showPos").SetValue(globalTarget);
        }
        catch
        {
            // Not an NBackButton; fall through to the plain write.
        }
        if (back.Visible && back.GlobalPosition.DistanceTo(globalTarget) > 3f)
            back.GlobalPosition = globalTarget;
    }

    internal static void After(Node node, double delay, Action action)
    {
        node.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                action();
        };
    }

    // Apply-now-and-keep-asserting: one tick after _Ready (the parent is no
    // longer busy adding children), a fast burst that outpaces the game's
    // intro tweens (the visible one-to-two-second "vanilla first, then the
    // patch snaps in" came from quarter-second entry delays plus half-second
    // re-assert ticks), then a calm steady tick for the node's lifetime.
    internal static void AssertLoop(Node node, Action action)
    {
        var ticks = 0;
        var reported = false;
        // Re-arming through the node's own tree handle dies the moment the
        // node is detached; the main loop's tree outlives every screen.
        void Rearm(double delay)
        {
            if (Engine.GetMainLoop() is SceneTree tree)
                tree.CreateTimer(delay).Timeout += Run;
        }
        void Run()
        {
            if (!GodotObject.IsInstanceValid(node))
                return;
            // Overlay-stack screens are DETACHED, not freed, while something
            // covers them and during run restore. A tick landing in that
            // window must idle and re-arm, never end the chain: this exact
            // early-return killed the rewards pass whenever a run resumed
            // straight into the loot screen, leaving it native for good.
            if (!node.IsInsideTree())
            {
                Rearm(0.5);
                return;
            }
            // One throwing tick must not kill the chain: these loops carry
            // whole screens, and a transient error (a node freed mid-walk)
            // used to silently end the layout for the rest of the scene.
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (!reported)
                {
                    reported = true;
                    PatchHelper.Log(
                        $"[Portrait] assert loop error on {node.Name}: {ex.GetBaseException().Message}"
                    );
                }
            }
            ticks++;
            Rearm(ticks < 14 ? 0.12 : 0.5);
        }

        // The first pass runs INLINE. Deferring it by even one timer tick means
        // the frame that gets drawn is the game's landscape layout, and the
        // screen visibly snaps into place a few frames later; that flash was
        // present on nearly every patched screen. The self-rearming ticks above
        // stay exactly as they were and remain the convergence backstop for
        // anything the game moves after we place it.
        //
        // Inline is only legal on the main thread with the node already in the
        // tree: AddChildSafely defers the add whenever the caller is off-thread
        // or the parent is mid-ready, and running early would hit the guard
        // above and end the chain before it ever re-armed.
        //
        // RULE for every pass reached this way: mutate only the patched node
        // and its descendants, never its parent. During NOTIFICATION_READY the
        // node's own block flag is clear, but its parent's is not.
        if (OS.GetThreadCallerId() == OS.GetMainThreadId()
            && GodotObject.IsInstanceValid(node)
            && node.IsInsideTree())
        {
            Run();
            return;
        }

        Rearm(0.02);
    }
}

internal sealed class PortraitCombatFrame : Control
{
    private Vector2 _configuredSize;
    private float _configuredTopSolid;

    internal void Configure(Vector2 canvas)
    {
        // The top band covers the authored sky strip (fixed art height) and
        // must also back the display cutout, whose inset can be reported late
        // or change between devices, so it is part of the rebuild signature.
        var topSolid = PortraitHudMetrics.CombatTopBandHeight(PortraitDisplay.SafeTop());
        if (_configuredSize == canvas
            && Math.Abs(_configuredTopSolid - topSolid) < 0.5f
            && GetChildCount() > 0)
            return;

        _configuredSize = canvas;
        _configuredTopSolid = topSolid;
        foreach (var child in GetChildren())
            child.QueueFree();

        var width = Math.Max(1f, canvas.X);
        var height = Math.Max(1f, canvas.Y);
        var topFade = Math.Max(430f, topSolid + 120f);
        const float bottomFade = 610f;
        const float bottomSolid = 235f;
        var topInk = new Color(0.027f, 0.145f, 0.094f, 1f);
        var bottomInk = new Color(0.055f, 0.095f, 0.034f, 1f);

        // The landscape background contains an authored blue-grey sky strip.
        // In portrait that strip reads as a second system bar, so cover it
        // completely before fading back into the arena artwork. Runtime C#
        // Controls do not reliably receive _Draw on this Android template, so
        // use real child CanvasItems instead of a custom drawing callback.
        AddBand(0f, topSolid, width, topInk);
        AddFadeBands(topSolid, topFade, width, topInk, fadeDown: true);

        // Likewise, replace the desktop hand tray rather than letting its grey
        // rectangle show through around the portrait card fan.
        AddFadeBands(height - bottomFade, height - bottomSolid, width, bottomInk, fadeDown: false);
        AddBand(height - bottomSolid, bottomSolid, width, bottomInk);
    }

    private void AddFadeBands(float start, float end, float width, Color color, bool fadeDown)
    {
        const int count = 8;
        var bandHeight = (end - start) / count;
        for (var index = 0; index < count; index++)
        {
            var strength = fadeDown
                ? 1f - (index + 1f) / (count + 1f)
                : (index + 1f) / (count + 1f);
            AddBand(
                start + index * bandHeight,
                bandHeight + 1f,
                width,
                new Color(color.R, color.G, color.B, strength * 0.92f)
            );
        }
    }

    private void AddBand(float y, float height, float width, Color color)
    {
        AddChild(new ColorRect
        {
            Position = new Vector2(0f, y),
            Size = new Vector2(width, height),
            Color = color,
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }
}

[HarmonyPatch(typeof(NGame), "ApplyDisplaySettings")]
internal static class ApplyDisplaySettingsPatch
{
    private static void Postfix() => PortraitDisplay.Apply();
}

// On Android the original bodies must NEVER run: Apply() can miss a frame
// (WindowGetSize is transient during scene swaps) and one pass-through was
// enough to lay the whole menu out as a landscape strip with dead input
// after Save and Quit. Portrait owns the window unconditionally.
[HarmonyPatch(typeof(NGame), "OnWindowChange")]
internal static class GameWindowChangePatch
{
    private static bool Prefix()
    {
        var applied = PortraitDisplay.Apply();
        return !OperatingSystem.IsAndroid() && !applied;
    }
}

// The menu writes the window's content scale ITSELF (KeepWidth 2580x1080 on
// wide, KeepHeight 1680x1260 on tall): after Save and Quit that fit won the
// frame and the whole menu rendered as a landscape strip with dead input,
// while every later property read showed our values. Portrait owns the
// window; the menu's own fit never runs.
[HarmonyPatch(typeof(NMainMenu), "OnWindowChange")]
internal static class MainMenuAspectWritePatch
{
    private static bool Prefix()
    {
        var applied = PortraitDisplay.Apply();
        return !OperatingSystem.IsAndroid() && !applied;
    }
}

[HarmonyPatch(typeof(NGlobalUi), "OnWindowChange")]
internal static class GlobalUiWindowChangePatch
{
    private static bool Prefix()
    {
        var applied = PortraitDisplay.Apply();
        return !OperatingSystem.IsAndroid() && !applied;
    }
}

internal static class PortraitMainMenu
{
    private const float BackgroundHeight = 1200f;
    private const float BackgroundWidth = 2560f;

    // The authored rows are 50 units tall, which lands around 6mm on the phone:
    // readable, but well under a comfortable thumb target. A primary menu is
    // where a touch layout should be most generous, so aim above the ergonomic
    // minimum rather than at it.
    // Above the corrected 160-unit floor, tuned by eye from there.
    private const float MenuRowTarget = 168f;
    private const float MenuRowFallback = 50f;
    private const float MenuScaleMin = 1.65f;
    private const float MenuScaleMax = 3.2f;
    private const float MenuRowSeparation = 10f;

    // Rows are resized in place, so the authored height has to be remembered:
    // reading Size again after a pass would compound the growth every time the
    // layout is re-applied.
    private static readonly StringName AuthoredSizeMeta = "sts2portrait_authored_size";
    private static readonly StringName AuthoredFontMeta = "sts2portrait_authored_font";

    // Where the logo stops being art and the screen becomes free space. The
    // button block is centred in what is left, so it neither collides with the
    // logo nor leaves the bottom third of the screen visibly empty.
    private const float LogoBandBottomRatio = 0.36f;

    // Sit the block slightly above the middle of the free band: dead centre
    // reads as a gap under the logo and a gap over the skyline at once.
    // Raised from 0.36 when the multiplayer row left the menu: four rows at
    // the old bias floated high and left a dead band above the bottom edge.
    private const float MenuBlockBandBias = 0.88f;
    private const float LogoTopRatio = 0.18f;
    private const float LogoScale = 0.42f;
    private const float LogoCenterOffset = 460f;

    // The focus reticles are positioned by the game from unscaled label
    // metrics, so they need to know how far the menu was scaled up.
    internal static float LastMenuScale { get; private set; } = 1f;

    internal static void Apply(NMainMenu menu)
    {
        // The patch-time install runs before the scene tree exists; this call
        // is idempotent and the menu is on every boot path, so the bridge is
        // guaranteed a tree by the first assert tick here.
        PortraitTouchInput.Install();

        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        PortraitNodes.DumpRegionOnRequest(menu);

        var center = canvas * 0.5f;
        var parentScale = ApplyBackground(menu, canvas, center);
        ApplyLogo(menu, canvas, center, parentScale);
        ApplyButtons(menu, canvas, center);
        ApplyCornerButtons(menu, canvas);
    }

    private static float ApplyBackground(NMainMenu menu, Vector2 canvas, Vector2 center)
    {
        var background = menu.GetNodeOrNull<Control>("MainMenuBg/BgContainer");
        if (background is null)
            return 1f;

        var scale = Mathf.Max(canvas.X / BackgroundWidth, canvas.Y / BackgroundHeight) * 1.04f;
        background.PivotOffset = new Vector2(BackgroundWidth, BackgroundHeight) * 0.5f;
        background.Scale = Vector2.One * scale;
        background.Position = center - new Vector2(BackgroundWidth, BackgroundHeight) * 0.5f;
        return scale;
    }

    private static void ApplyLogo(NMainMenu menu, Vector2 canvas, Vector2 center, float parentScale)
    {
        if (menu.FindChild("Logo", recursive: true, owned: false) is not Node2D logo)
            return;

        logo.Scale = Vector2.One * (LogoScale / parentScale);
        logo.GlobalPosition = new Vector2(center.X - LogoCenterOffset, canvas.Y * LogoTopRatio);
        logo.Visible = true;
        logo.Modulate = new Color(logo.Modulate.R, logo.Modulate.G, logo.Modulate.B, 1f);
    }

    // The button tweens its label's scale on hover and back to one on release,
    // so anything written to that property is wiped the first time a finger
    // touches the row. Growing the font instead survives, and the game's own
    // 1.05 hover pop keeps working on top of it.
    private static void GrowFont(Label label, float scale)
    {
        const string fontSizeConstant = "font_size";
        var authored = label.GetMeta(AuthoredFontMeta, Variant.From(0)).AsInt32();
        if (authored <= 0)
        {
            authored = label.GetThemeFontSize(fontSizeConstant);
            if (authored <= 0)
                return;

            label.SetMeta(AuthoredFontMeta, authored);
        }

        var target = Mathf.RoundToInt(authored * scale);
        if (label.HasThemeFontSizeOverride(fontSizeConstant)
            && label.GetThemeFontSize(fontSizeConstant) == target)
            return;

        label.AddThemeFontSizeOverride(fontSizeConstant, target);
    }

    private static void SetIfChanged(Control control, Vector2 size)
    {
        if (control.CustomMinimumSize.DistanceTo(size) <= 0.5f)
            return;

        control.CustomMinimumSize = size;
    }

    // The profile and patch-notes buttons are authored 16 units from the top
    // of a landscape frame, which on this phone is inside the cutout band, at
    // 64 units tall, well under a thumb target, and hard against the screen
    // edges. Drop them below the safe inset, grow them, and inset them.
    private const float CornerButtonMaxScale = 1.9f;
    private const float CornerButtonTopGap = 18f;
    private const string CornerGrownMeta = "Sts2PortraitCornerGrown";

    private static void ApplyCornerButtons(NMainMenu menu, Vector2 canvas)
    {
        var top = PortraitDisplay.SafeTop() + CornerButtonTopGap;
        PlaceCornerButton(PortraitNodes.FindControl(menu, "ChangeProfileButton"), canvas, top, onRight: false);
        PlaceCornerButton(PortraitNodes.FindControl(menu, "PatchNotesButton"), canvas, top, onRight: true);
    }

    private static void PlaceCornerButton(Control button, Vector2 canvas, float top, bool onRight)
    {
        if (button is null)
            return;

        // The button's own Scale is off limits: the profile button writes
        // Scale 1.02 on focus and tweens it back to 1 on unfocus, and the
        // hover that precedes a touch press shrank a scaled button out from
        // under the finger (no tap ever landed). Patching those methods is
        // not an option either, their bodies call protected base members
        // (BUG-020). So the growth lives one level down: the children are
        // scaled and spread about the button's origin and the button's Size
        // grows with them, once; the game's 1.02 then rides on top harmlessly.
        var width = button.Size.X > 1f ? button.Size.X : 64f;
        var height = button.Size.Y > 1f ? button.Size.Y : 64f;
        var scale = PortraitHudMetrics.TouchScale(width, height, CornerButtonMaxScale);
        if (!button.HasMeta(CornerGrownMeta))
        {
            foreach (var child in button.GetChildren())
            {
                if (child is not Control c)
                    continue;
                c.PivotOffset = Vector2.Zero;
                c.Position *= scale;
                c.Scale *= scale;
            }
            PortraitNodes.ClearAnchors(button);
            button.Size = new Vector2(width, height) * scale;
            button.SetMeta(CornerGrownMeta, scale);
        }
        var grown = (float)button.GetMeta(CornerGrownMeta);

        PortraitNodes.ClearAnchors(button);
        var target = new Vector2(
            onRight
                ? canvas.X - width * grown - PortraitHudMetrics.EdgeMargin
                : PortraitHudMetrics.EdgeMargin,
            top
        );
        if (button.GlobalPosition.DistanceTo(target) > 1.5f)
            button.GlobalPosition = target;
    }

    private static Vector2 AuthoredSize(Control row)
    {
        var authored = row.GetMeta(AuthoredSizeMeta, Variant.From(Vector2.Zero)).AsVector2();
        if (authored.Y > 1f)
            return authored;

        authored = row.Size.Y > 1f ? row.Size : new Vector2(200f, MenuRowFallback);
        row.SetMeta(AuthoredSizeMeta, authored);
        return authored;
    }

    // Rows are measured from the first visible entry rather than assumed, so a
    // future menu item with a different height still gets a thumb-sized row.
    // The measurement uses the remembered authored height: reading the live
    // size would shrink the scale to 1 on the second pass, because the row has
    // already been grown by the first.
    private static float MenuScale(Control buttons, Vector2 canvas)
    {
        var row = MenuRowFallback;
        var rows = 0;
        foreach (var child in buttons.GetChildren())
        {
            if (child is not Control { Visible: true } control)
                continue;

            var authored = AuthoredSize(control);
            if (authored.Y <= 1f)
                continue;

            if (rows == 0)
                row = authored.Y;
            rows++;
        }

        // The thumb target says 168 per row; the band under the logo says how
        // many of those fit. On a 16:9 phone the fixed scale ran the last row
        // off the bottom edge, so the band wins when it is the tighter one.
        var bandTop = canvas.Y * LogoBandBottomRatio;
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());
        var need = Math.Max(rows, 1) * row + Math.Max(rows - 1, 0) * MenuRowSeparation;
        var fit = need > 0f ? (bandBottom - bandTop) / need : MenuScaleMax;
        return Mathf.Clamp(Math.Min(MenuRowTarget / row, fit), MenuScaleMin, MenuScaleMax);
    }

    private static void ApplyButtons(NMainMenu menu, Vector2 canvas, Vector2 center)
    {
        var buttons = menu.GetNodeOrNull<Control>("MainMenuTextButtons")
            ?? menu.GetNodeOrNull<Control>("%MainMenuTextButtons");
        if (buttons is null)
            return;

        // This pass owns every row in the container; without the claim the
        // global touch sweep grows the same rows it just sized and the menu
        // visibly breathes as the two passes alternate.
        PortraitTouchPass.MarkManaged(buttons);

        var scale = MenuScale(buttons, canvas);
        LastMenuScale = scale;

        if (buttons is BoxContainer box)
            box.AddThemeConstantOverride("separation", (int)(MenuRowSeparation * scale));

        // Grow the rows themselves instead of scaling the container. A scaled
        // container still reports its authored size, and the game hit-tests
        // buttons against that rect, so a visually large menu was only
        // clickable in a small patch at its top-left corner.
        var rowWidth = 0f;
        var rowTotal = 0f;
        var rows = 0;
        foreach (var child in buttons.GetChildren())
        {
            // Multiplayer needs a Steam client runtime the phone does not
            // have (the launcher's SteamKit login only authenticates depot
            // downloads, it is no lobby backend), so the row was a guaranteed
            // dead end on device; the portrait menu drops it.
            if (child is Control mpRow && mpRow.Name == "MultiplayerButton")
            {
                mpRow.Visible = false;
                continue;
            }
            if (child is not Control { Visible: true } row)
                continue;

            var authored = AuthoredSize(row);
            // Re-applied every frame by the layout guard: assigning the same
            // values again re-sorts the container, which makes the button lose
            // the press it was in the middle of. Only write real changes.
            SetIfChanged(row, authored * scale);
            rowWidth = Mathf.Max(rowWidth, authored.X * scale);
            rowTotal += authored.Y * scale;
            rows++;

            if (row.GetChildCount() > 0 && row.GetChild(0) is Label label)
                GrowFont(label, scale);
        }

        PortraitNodes.ClearAnchors(buttons);
        buttons.PivotOffset = Vector2.Zero;
        buttons.Scale = Vector2.One;
        // The VBox carries an authored minimum height (1306 on the 16:9
        // check) and centers its rows inside it, so the block hung 285 lower
        // than the placement below assumed and the last row left the screen
        // on shorter phones. The block is exactly its rows.
        if (buttons.CustomMinimumSize != Vector2.Zero)
            buttons.CustomMinimumSize = Vector2.Zero;
        if (buttons is BoxContainer menuBox && menuBox.Alignment != BoxContainer.AlignmentMode.Begin)
            menuBox.Alignment = BoxContainer.AlignmentMode.Begin;
        var blockHeight = rows > 0
            ? rowTotal + MenuRowSeparation * scale * (rows - 1)
            : buttons.Size.Y;
        var blockWidth = rowWidth > 1f ? rowWidth : buttons.Size.X;
        SetIfChanged(buttons, new Vector2(blockWidth, blockHeight));

        var bandTop = canvas.Y * LogoBandBottomRatio;
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());
        var free = Mathf.Max(0f, bandBottom - bandTop - blockHeight);
        var top = bandTop + free * MenuBlockBandBias;

        // Position is parent relative and this container does not sit at the
        // scene origin, so drive the global corner instead of assuming it.
        var target = new Vector2(center.X - blockWidth * 0.5f, top);
        if (buttons.GlobalPosition.DistanceTo(target) > 0.5f)
        {
            buttons.Position += target - buttons.GlobalPosition;
            LogRowGeometry(buttons, scale);
        }
    }

    // Layout maths for this menu has been wrong twice in ways a screenshot
    // cannot distinguish, so the applied geometry is reported once per pass.
    private static void LogRowGeometry(Control buttons, float scale)
    {
        foreach (var child in buttons.GetChildren())
        {
            if (child is not Control { Visible: true } row)
                continue;

            var label = row.GetChildCount() > 0 ? row.GetChild(0) as Control : null;
            PatchHelper.Log(
                $"[Portrait] menu row {row.Name}: scale={scale:F2} rect={row.GlobalPosition.X:F0},{row.GlobalPosition.Y:F0} {row.Size.X:F0}x{row.Size.Y:F0}"
                + (label is null
                    ? " label=none"
                    : $" label={label.GlobalPosition.X:F0},{label.GlobalPosition.Y:F0} {label.Size.X:F0}x{label.Size.Y:F0} s={label.Scale.X:F2}")
            );
        }
    }
}

[HarmonyPatch(typeof(NMainMenu), "_Ready")]
internal static class MainMenuReadyPatch
{
    private static void Prefix()
    {
        if (SaveManager.Instance?.SettingsSave is { } settings)
            settings.SeenEaDisclaimer = true;
    }

    private static void Postfix(NMainMenu __instance)
    {
        // The Save-and-Quit return path drifts the NATIVE content scale
        // landscape while the C# cache stays correct; force the native
        // write here, where every menu (re)build passes through.
        PortraitDisplay.ForceRefresh();
        PortraitNodes.After(__instance, 2.0, () =>
        {
            var canvas = PortraitDisplay.CanvasSize;
            if (PortraitDisplay.IsPortrait(canvas))
                PortraitNodes.LogEdgePeekers(__instance.GetTree().Root, canvas);
        });
        PortraitNodes.AssertLoop(__instance, () =>
        {
            PortraitMainMenu.Apply(__instance);
            // The run-scene watermark sweep rides the top-bar reflow, which
            // never runs on the menu scene; sweep here for the menu and its
            // submenus (compendium included).
            var canvas = PortraitDisplay.CanvasSize;
            if (PortraitDisplay.IsPortrait(canvas))
                PortraitTopBar.HideBuildWatermark(__instance.GetTree().Root, canvas);
        });
    }
}


// Character select in portrait left a dead band at the top where the menu logo
// showed through, kept the back button partly off the left edge, and gave the
// character row less height than a thumb needs. The art fills the screen from
// the top instead, and the controls sit inside the canvas at touch size.
internal static class PortraitCharacterSelect
{
    private const float ArtWidth = 2560f;
    private const float ArtHeight = 1200f;

    // How much of the screen the character art takes. Enough to read as the
    // subject of the screen, not so much that the head is cropped away.
    private const float ArtCoverRatio = 0.70f;

    // The Spine character is drawn near the top of its authored frame, so a
    // frame pinned at y=0 clips the helmet. Push the frame down by the cutout
    // band and let it overshoot the bottom instead.
    private const float ArtTopOffset = 150f;
    private const float ArtMaxScale = 1.8f;

    private const float InfoPanelBottomGap = 48f;
    private const float ArtToRowGap = 70f;
    private const float RowToNavGap = 44f;
    private const float CharacterRowScale = 1.3f;
    private const float NavButtonWidth = 200f;
    private const float NavButtonHeight = 110f;

    internal static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var artBottom = ApplyArt(screen, canvas);
        var rowBottom = ApplyCharacterRow(screen, canvas, artBottom);
        ApplyNavButtons(screen, canvas, rowBottom);
        ApplyInfoPanel(screen, canvas, artBottom);
    }

    // The art node is authored for a 2560x1200 landscape frame, so in portrait
    // it only ever covered a middle strip and the main menu showed through
    // above it, logo included. Pin it to the top and let it own the upper
    // two thirds.
    private static float ApplyArt(Control screen, Vector2 canvas)
    {
        var target = canvas.Y * ArtCoverRatio;
        var scale = Mathf.Min(Mathf.Max(target / ArtHeight, canvas.X / ArtWidth), ArtMaxScale);

        foreach (var name in new[] { "AnimatedBg", "StaticBg" })
        {
            if (PortraitNodes.FindControl(screen, name) is not { } art)
                continue;

            PortraitNodes.ClearAnchors(art);
            art.PivotOffset = new Vector2(ArtWidth, ArtHeight) * 0.5f;
            art.Scale = Vector2.One * scale;
            var artCenter = new Vector2(canvas.X * 0.5f, ArtTopOffset + ArtHeight * scale * 0.5f);
            art.Position += artCenter - (art.GlobalPosition + new Vector2(ArtWidth, ArtHeight) * 0.5f);
        }

        return ArtTopOffset + ArtHeight * scale;
    }

    // The character row used to sit on the very bottom edge, which is both the
    // hardest place to reach and where the gesture bar lives. It rides under
    // the art instead, with the whole control block kept off the bottom.
    private static float ApplyCharacterRow(Control screen, Vector2 canvas, float artBottom)
    {
        var row = PortraitNodes.FindControl(screen, "ButtonContainer");
        if (row is null)
            return artBottom;

        PortraitNodes.ClearAnchors(row);
        var width = row.Size.X > 1f ? row.Size.X : 564f;
        var height = row.Size.Y > 1f ? row.Size.Y : 154f;
        row.PivotOffset = Vector2.Zero;
        row.Scale = Vector2.One * CharacterRowScale;

        var scaledWidth = width * CharacterRowScale;
        var scaledHeight = height * CharacterRowScale;
        var contentBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());
        var navBlock = RowToNavGap + NavButtonHeight;
        var top = Mathf.Min(
            artBottom + ArtToRowGap,
            contentBottom - navBlock - scaledHeight
        );

        var target = new Vector2(canvas.X * 0.5f - scaledWidth * 0.5f, top);
        row.Position += target - row.GlobalPosition;
        row.ZAsRelative = false;
        row.ZIndex = 60;
        return top + scaledHeight;
    }

    // Both nav buttons were authored outside the portrait canvas: back sat at
    // x=-220 and was cut in half by the screen edge.
    private static void ApplyNavButtons(Control screen, Vector2 canvas, float rowBottom)
    {
        var y = rowBottom + RowToNavGap;
        foreach (var (name, onRight) in new[] { ("BackButton", false), ("ConfirmButton", true) })
        {
            if (PortraitNodes.FindControl(screen, name) is not { } button)
                continue;

            PortraitNodes.ClearAnchors(button);
            var width = button.Size.X > 1f ? button.Size.X : NavButtonWidth;
            var x = onRight
                ? canvas.X - width - PortraitHudMetrics.EdgeMargin
                : PortraitHudMetrics.EdgeMargin;
            button.Position += new Vector2(x, y) - button.GlobalPosition;
            button.ZAsRelative = false;
            button.ZIndex = 70;
        }
    }

    private static void ApplyInfoPanel(Control screen, Vector2 canvas, float artBottom)
    {
        var panel = PortraitNodes.FindControl(screen, "InfoPanel");
        if (panel is null)
            return;

        PortraitNodes.ClearAnchors(panel);
        var height = panel.Size.Y > 1f ? panel.Size.Y : 434f;
        var y = artBottom - height - InfoPanelBottomGap;
        panel.Position += new Vector2(PortraitHudMetrics.EdgeMargin, y) - panel.GlobalPosition;
        panel.ZAsRelative = false;
        panel.ZIndex = 50;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), "InitializeSingleplayer")]
internal static class CharacterSelectInitializePatch
{
    private static void Postfix(Control __instance)
        => PortraitNodes.AssertLoop(__instance, () => PortraitCharacterSelect.Apply(__instance));
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), "OnSubmenuOpened")]
internal static class CharacterSelectOpenedPatch
{
    private static void Postfix(Control __instance)
        => PortraitNodes.AssertLoop(__instance, () => PortraitCharacterSelect.Apply(__instance));
}


// The gold focus reticles are placed from the label's global position, which
// follows our scaled menu, but padded with the label's unscaled width. On a
// Patch notes: the back tab is authored bottom-left and sat on the article
// text; settings and pause moved theirs to the top, so this one follows.
// The article gets a top margin equal to the tab band so the date line does
// not start under the tab.
[HarmonyPatch(typeof(NPatchNotesScreen), nameof(NPatchNotesScreen.Open))]
internal static class PatchNotesOpenPatch
{
    private const string MarginMeta = "Sts2PortraitPatchNotesMargin";
    // Measured: the date label starts 5 units below the content top and the
    // back tab ends near 256; 280 puts the date under the tab with a gap.
    private const float TabBand = 280f;

    // Prefix: Open() enables the back button, and OnEnable captures _showPos
    // into a tween right there. Rewriting the field afterwards leaves that
    // tween heading for the authored bottom-left corner.
    private static void Prefix(NPatchNotesScreen __instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        try
        {
            var back = PortraitNodes.FindControl(__instance, "BackButton");
            PortraitNodes.PlaceBackTab(
                back,
                new Vector2(PortraitHudMetrics.EdgeMargin, PortraitDisplay.SafeTop() + 6f)
            );
            // Several nodes are named Content; the article's margin box is the
            // parent of the patch text.
        }
        catch (Exception e)
        {
            PatchHelper.Log($"[Portrait] patch notes back tab failed: {e.Message}");
        }
    }

    // The article scrolls inside ScreenContents; start that box below the tab
    // band instead of under the tab. Postfix: Open() lays the text out first.
    private static void Postfix(NPatchNotesScreen __instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        try
        {
            // NScrollableContainer drives its Content's Y from _paddingTop
            // (scroll-to-top and drag both write Position from it), so offsets
            // and margins on the box never moved the text. Raise the padding by
            // the tab band and put the content there; the container's own
            // scroll-to-top then agrees with us.
            if (PortraitNodes.FindControl(__instance, "ScreenContents") is { } contents
                && !contents.HasMeta(MarginMeta))
            {
                var t = Traverse.Create(contents);
                var content = t.Field("_content").GetValue<Control>();
                if (content is not null)
                {
                    var authoredTop = content.Position.Y;
                    var bottom = (float)t.Field("_paddingBottom").GetValue();
                    t.Method("UpdatePadding", new object[] { authoredTop + TabBand, bottom }).GetValue();
                    content.Position = new Vector2(content.Position.X, authoredTop + TabBand);
                }
                contents.SetMeta(MarginMeta, true);
            }
        }
        catch (Exception e)
        {
            PatchHelper.Log($"[Portrait] patch notes pass failed: {e.Message}");
        }
    }
}

// touch-sized menu that puts the right one inside the text and leaves both at
// a fraction of the button's height. Re-place and re-size them after the game
// has started its own tween, and drop that tween so it cannot pull them back.
[HarmonyPatch(typeof(NMainMenu), "MainMenuButtonFocused")]
internal static class MainMenuReticlePatch
{
    private const float ReticleGap = 34f;

    private static void Postfix(NMainMenu __instance, object button)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        try
        {
            var scale = PortraitMainMenu.LastMenuScale;
            if (scale <= 1.01f)
                return;

            if (AccessTools.Field(typeof(NMainMenu), "_reticleTween")?.GetValue(__instance) is Tween tween)
                tween.Kill();

            var label = AccessTools.Field(button.GetType(), "label")?.GetValue(button) as Control;
            var left = AccessTools.Field(typeof(NMainMenu), "_buttonReticleLeft")?.GetValue(__instance) as Control;
            var right = AccessTools.Field(typeof(NMainMenu), "_buttonReticleRight")?.GetValue(__instance) as Control;
            if (label is null || left is null || right is null)
                return;

            // The label is grown through its font size now, so its own rect is
            // already the visible one; only the reticle art still needs scaling.
            var labelWidth = label.Size.X;
            var labelHeight = label.Size.Y;
            var centerY = label.GlobalPosition.Y + labelHeight * 0.5f;

            Place(left, scale, label.GlobalPosition.X - ReticleGap * scale, centerY, onRight: false);
            Place(right, scale, label.GlobalPosition.X + labelWidth + ReticleGap * scale, centerY, onRight: true);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] Menu reticle placement failed: {ex.GetBaseException().Message}");
        }
    }

    private static void Place(Control reticle, float scale, float edgeX, float centerY, bool onRight)
    {
        reticle.PivotOffset = Vector2.Zero;
        reticle.Scale = Vector2.One * scale;
        var width = reticle.Size.X * scale;
        var height = reticle.Size.Y * scale;
        reticle.GlobalPosition = new Vector2(
            onRight ? edgeX : edgeX - width,
            centerY - height * 0.5f
        );
        reticle.Modulate = new Color(reticle.Modulate, 1f);
    }
}


// The pause menu keeps the authored desktop block: narrow rows in the middle
// of the screen, well under thumb size on the phone. Rows are grown to touch
// size, the block is centred, and the game's own label autosizing is re-run so
// the text fills the bigger rows. The scene also defaults every button to
// visible, so when the game's client/host rule does not run, both quit
// variants show at once; singleplayer keeps Save and Quit.
internal static class PortraitPauseMenu
{
    // Rows take a share of the canvas width rather than a fixed 720, so a
    // narrower or wider phone gets the same proportions; at 1180 wide this
    // is 968. Heights and the font ceiling grew with the user's read that
    // the earlier plates were still small for a thumb.
    private const float ButtonWidthShare = 0.82f;
    private const float ButtonHeight = 200f;
    private const float RowSeparation = 30f;
    private const float TitleGap = 64f;
    private const float TitleScale = 1.5f;

    internal static void Apply(Control menu)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var container = PortraitNodes.FindControl(menu, "ButtonContainer");
        if (container is null)
            return;

        // This pass sizes every row; the global sweep must not re-grow them.
        PortraitTouchPass.MarkManaged(container);

        var saveAndQuit = PortraitNodes.FindControl(container, "SaveAndQuit");
        var disconnect = PortraitNodes.FindControl(container, "Disconnect");
        if (saveAndQuit is { Visible: true } && disconnect is { Visible: true })
            disconnect.Visible = false;

        if (container is BoxContainer box)
            box.AddThemeConstantOverride("separation", (int)RowSeparation);

        var buttonWidth = canvas.X * ButtonWidthShare;
        var rows = 0;
        foreach (var child in container.GetChildren())
        {
            if (child is not Control { Visible: true } row)
                continue;

            row.CustomMinimumSize = new Vector2(buttonWidth, ButtonHeight);
            // The rows came out narrower than the container and the VBox
            // left-aligned them, so the whole stack sat left of the title;
            // center each row inside the container instead.
            row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            // The plate art and label do not follow the grown row since a
            // game update: the label autosized to the full row while the
            // plate stayed authored-size, so "Compendium" and "Save and
            // Quit" spilled past their buttons. Pin both to the row rect.
            if (row.GetNodeOrNull<Control>("ButtonImage") is { } art)
            {
                FillRowRect(art);
                // The plate texture carries its drop shadow on the right, so
                // the art reads ~16 units left of the centered label; shift
                // the art, not the text.
                art.OffsetLeft = 16f;
                art.OffsetRight = 16f;
            }
            if (row.GetNodeOrNull<Control>("Label") is { } label)
            {
                FillRowRect(label);
                // Fitted to the grown plate the autosizer still stops at the
                // authored ceiling, leaving small text on a big button; lift
                // the ceiling once and let RefreshLabels re-fit below it.
                RaiseFontCeiling(label, 1.8f);
            }
            rows++;
        }

        if (rows == 0)
            return;

        var height = rows * ButtonHeight + (rows - 1) * RowSeparation;
        PortraitNodes.ClearAnchors(container);
        container.Size = new Vector2(buttonWidth, height);
        // Centering on the canvas left the lower 40% dead under four rows;
        // the block sits in the content band at the same lower bias the
        // main menu uses, so the thumb reaches it and the hole closes.
        var bandTop = PortraitHudMetrics.ContentTop(PortraitDisplay.SafeTop()) + TitleGap + 80f;
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom()) - 140f;
        var free = Mathf.Max(0f, bandBottom - bandTop - height);
        var top = bandTop + free * 0.62f;
        container.Position += new Vector2((canvas.X - buttonWidth) * 0.5f, top)
            - container.GlobalPosition;

        var title = PortraitNodes.FindControl(menu, "PausedText");
        if (title is not null)
        {
            PortraitNodes.ClearAnchors(title);
            var titleWidth = (title.Size.X > 1f ? title.Size.X : 440f) * TitleScale;
            var titleHeight = (title.Size.Y > 1f ? title.Size.Y : 64f) * TitleScale;
            title.PivotOffset = Vector2.Zero;
            title.Scale = Vector2.One * TitleScale;
            title.Position += new Vector2(
                canvas.X * 0.5f - titleWidth * 0.5f,
                top - TitleGap - titleHeight
            ) - title.GlobalPosition;
        }

        // The stack's back tab is shared with the settings screen opened
        // from here, and at the bottom left it landed on the settings list's
        // last rows (Credits). The top band is free on every capstone (the
        // top bar hides), so the tab lives top-left, mobile-header style.
        var back = PortraitNodes.FindControl(menu, "BackButton");
        if (back is not null)
        {
            PortraitNodes.ClearAnchors(back);
            PortraitNodes.PlaceBackTab(
                back,
                new Vector2(PortraitHudMetrics.EdgeMargin, PortraitDisplay.SafeTop() + 6f)
            );
        }

        RefreshLabels(menu);
    }

    private const string FontCapMeta = "Sts2PortraitPauseFontCap";

    private static void RaiseFontCeiling(Control label, float factor)
    {
        if (label.HasMeta(FontCapMeta))
            return;
        label.SetMeta(FontCapMeta, true);
        try
        {
            var prop = label.GetType().GetProperty("MaxFontSize");
            if (prop?.GetValue(label) is int cap)
                prop.SetValue(label, (int)(cap * factor));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] pause font ceiling raise failed: {ex.GetBaseException().Message}");
        }
    }

    private static void FillRowRect(Control child)
    {
        if (child is null)
            return;
        child.AnchorLeft = 0f;
        child.AnchorTop = 0f;
        child.AnchorRight = 1f;
        child.AnchorBottom = 1f;
        child.OffsetLeft = 0f;
        child.OffsetTop = 0f;
        child.OffsetRight = 0f;
        child.OffsetBottom = 0f;
        if (child is TextureRect art)
        {
            art.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            art.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
    }

    // Labels size their font to the rect they had when the text was set, so
    // after the rows grow the game's own refresh has to run again for the
    // text to fill the bigger buttons.
    private static void RefreshLabels(Control menu)
    {
        try
        {
            AccessTools.Method(menu.GetType(), "RefreshLabels")?.Invoke(menu, null);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] Pause label refresh failed: {ex.GetBaseException().Message}");
        }
    }
}

// Initialize is the safe hook here: it is public, runs every time the pause
// menu comes up, and its body touches nothing a patched copy cannot reach.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu), "Initialize")]
internal static class PauseMenuPatch
{
    private static void Postfix(Control __instance)
        => PortraitNodes.AssertLoop(__instance, () => PortraitPauseMenu.Apply(__instance));
}

// Initialize runs once per run (NGlobalUi.Initialize); the pause menu is a
// capstone submenu shown and hidden many times after that, and on a pause
// opened mid-combat the rows came up at their authored 372x80 with the tab
// bottom-left: the loop from Initialize was gone. OnSubmenuOpened runs on
// every open; its body is a public base call plus a public singleton call.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu), "OnSubmenuOpened")]
internal static class PauseMenuOpenedPatch
{
    private static void Postfix(Control __instance)
        => PortraitNodes.AssertLoop(__instance, () => PortraitPauseMenu.Apply(__instance));
}

[HarmonyPatch(typeof(NMainMenuBg), "OnWindowChange")]
internal static class MainMenuWindowChangePatch
{
    private static void Postfix(NMainMenuBg __instance)
    {
        for (Node node = __instance; node is not null; node = node.GetParent())
        {
            if (node is NMainMenu menu)
            {
                PortraitMainMenu.Apply(menu);
                return;
            }
        }
    }
}

internal static class PortraitCapstone
{
    // The capstone container keeps permanent furniture visible (backstop
    // ColorRect, submenu stack, screen proxy), so "any visible child" reads
    // as always-open. Real screens appear in two distinct places: pause and
    // settings as children of the stack's "Submenus" node, deck view as a
    // direct child of the container itself.
    // The loot screen lives outside the capstone container, but the fan's
    // absolute Z drew leftover hand cards straight over it all the same.
    // It is deliberately NOT part of IsOpen: the rewards layout pass skips
    // itself while a capstone is open, and folding the rewards screen into
    // IsOpen made that pass skip itself forever.
    internal static bool EclipsesCombatHud(Node anchor)
    {
        if (anchor is null || !GodotObject.IsInstanceValid(anchor) || !anchor.IsInsideTree())
            return false;

        if (PortraitNodes.FindByType(anchor.GetTree().Root, "NRewardsScreen") is { Visible: true })
            return true;

        // Skipping the loot opens the map while the finished combat scene is
        // still alive underneath; the fan and End Turn drew over the map.
        if (PortraitSceneCache.FindByType(anchor.GetTree().Root, "NMapScreen")
                is Control { Visible: true } map && map.IsVisibleInTree())
            return true;

        return IsOpen(anchor);
    }

    internal static bool IsOpen(Node anchor)
    {
        if (anchor is null || !GodotObject.IsInstanceValid(anchor) || !anchor.IsInsideTree())
            return false;

        var capstone = PortraitSceneCache.FindByType(anchor.GetTree().Root, "NCapstoneContainer");
        if (capstone is null)
            return false;
        foreach (var child in capstone.GetChildren())
        {
            if (child is not Control { Visible: true } control)
                continue;
            var name = control.Name.ToString();
            if (name is "CapstoneBackstop" or "ActiveScreenProxy")
                continue;
            if (name == "CapstoneSubmenuStack")
            {
                if (PortraitNodes.FindControl(control, "Submenus") is { } submenus)
                {
                    foreach (var sub in submenus.GetChildren())
                    {
                        if (sub is Control { Visible: true })
                            return true;
                    }
                }
                continue;
            }
            return true;
        }
        return false;
    }
}

internal static class PortraitCombat
{
    private const string FrameName = "Sts2PortraitCombatFrame";
    private const string BackgroundBasePositionMeta = "sts2_portrait_background_base_position";
    // Cover 1920x1080 combat art on the tall 1180x2596 portrait canvas,
    // with enough overscan to crop the authored sky and floor edge bands.
    private const float BackgroundScale = 2.62f;
    // Half a card of art in holder-local units, plus the margin the fan keeps
    // from the screen edges; both feed the fan budget.
    private const float FanCardHalf = 148f;
    private const float FanEdgePad = 26f;

    // Effective global scale of the hand holder, recorded by PlaceHand. Card
    // positions from the game's table are holder-local, so the budget has to
    // be converted through the live scale instead of guessing it.
    internal static float FanEffectiveScale { get; private set; } = 1f;
    private const float HandBaseline = 0.925f;

    internal static void ScaleBackground(object instance)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;

        var background = Traverse.Create(instance).Field("_bgContainer").GetValue<Control>();
        if (background is not null)
        {
            // Scale around a shallow vertical pivot. This crops the authored sky
            // strip off the top while adding coverage below; translating this
            // container breaks its internal clipped layers and exposes the clear
            // color through most of the arena.
            background.PivotOffset = new Vector2(0f, 105f);
            background.Scale = Vector2.One * BackgroundScale;
            if (!background.HasMeta(BackgroundBasePositionMeta))
                background.SetMeta(BackgroundBasePositionMeta, background.Position);
            var basePosition = (Vector2)background.GetMeta(BackgroundBasePositionMeta);
            // The blue-grey patch is inside the source painting rather than a
            // UI border. With ample vertical overscan already present, a small
            // upward crop replaces it with the dungeon wall below without
            // moving creatures or exposing an empty edge.
            background.Position = basePosition + new Vector2(0f, -88f);
        }
    }

    internal static float HandScaleFor(Control holder)
    {
        var visibleCards = 0;
        foreach (var child in holder.GetChildren())
        {
            if (child is CanvasItem { Visible: true })
                visibleCards++;
        }

        if (visibleCards <= 5)
            return 1.08f;

        return Mathf.Lerp(1.08f, 0.76f, Mathf.Clamp((visibleCards - 5f) / 5f, 0f, 1f));
    }

    // The authored fan is symmetric around zero, so scaling X by the ratio of
    // the available half-width to the widest authored X pulls the whole hand
    // inside the screen while keeping the spacing even.
    internal static float CompressFan(float x, float widestX, Vector2 canvas)
    {
        var scale = FanEffectiveScale > 0.05f ? FanEffectiveScale : 1f;
        var budget = (canvas.X * 0.5f - FanEdgePad) / scale - FanCardHalf;
        if (widestX <= budget || widestX <= 0f)
            return x;

        return x * (budget / widestX);
    }

    // Card art reaches roughly this far below the fan's baseline at hand
    // scale; the baseline must keep that plus the safe-area inset on screen.
    private const float HandBottomClearance = 240f;

    private const string HandGuardMeta = "sts2_portrait_hand_guard";
    private const string HandHiddenMeta = "sts2_portrait_hand_hidden_for_capstone";

    // Fullscreen capstone screens (deck view, in-run settings) sit at plain
    // tree z, and the fan's absolute ZIndex would draw the cards over them.
    // Hide the holder while such a screen is open; restore only what we hid.
    // Everything the portrait layer lifted to an absolute ZIndex ignores the
    // scene order of fullscreen capstones, so each of these has to be hidden
    // with the hand and restored afterwards; the piles, energy and End Turn
    // used to draw straight over the settings screen.
    private static readonly string[] CapstoneHiddenHud =
    {
        "DrawPile", "DiscardPile", "EndTurnButton", "EnergyCounterContainer",
        FrameName,
    };

    private static void ApplyCapstoneHandVisibility(Node hand, Control holder)
    {
        var open = PortraitCapstone.EclipsesCombatHud(hand);

        if (open && holder.Visible)
        {
            holder.Visible = false;
            holder.SetMeta(HandHiddenMeta, true);
        }
        else if (!open && holder.HasMeta(HandHiddenMeta))
        {
            holder.RemoveMeta(HandHiddenMeta);
            holder.Visible = true;
        }

        var root = hand.GetTree()?.Root;
        if (root is null)
            return;

        // Same-state ticks are free: the expensive lookups only run when the
        // eclipse state flips.
        if (open == _lastEclipseState)
            return;
        _lastEclipseState = open;

        foreach (var name in CapstoneHiddenHud)
        {
            if (PortraitSceneCache.Find(root, name) is not { } control)
                continue;

            if (open && control.Visible)
            {
                control.Visible = false;
                control.SetMeta(HandHiddenMeta, true);
            }
            else if (!open && control.HasMeta(HandHiddenMeta))
            {
                control.RemoveMeta(HandHiddenMeta);
                control.Visible = true;
            }
        }
    }

    private static bool? _lastEclipseState;

    private static float HandBaselineY(Vector2 canvas)
        => Math.Min(
            canvas.Y * HandBaseline,
            canvas.Y - PortraitDisplay.SafeBottom() - HandBottomClearance
        );

    internal static void PlaceHand(Control holder, Vector2 canvas)
    {
        holder.Scale = Vector2.One * HandScaleFor(holder);
        // Anchor in GLOBAL space like every other placement helper: the
        // holder's parent is not at the canvas origin on every combat entry
        // path (console/room teleports differ from map clicks), so a raw
        // local Position put the fan mid-screen there. The X centering is
        // load-bearing too: the authored landscape scene leaves the fan's
        // origin left of the canvas centre, which pushed the outer cards off
        // one edge more than the other.
        holder.Position += new Vector2(
            canvas.X * 0.5f - holder.GlobalPosition.X,
            HandBaselineY(canvas) - holder.GlobalPosition.Y
        );
        FanEffectiveScale = holder.GetGlobalTransform().Scale.X;
        holder.ZAsRelative = false;
        // Above the combat frame's bands (100) but below every game screen and
        // overlay: forcing the old 320 here drew the fan over the in-combat
        // deck view.
        holder.ZIndex = 110;
    }

    // The card holder can be created AFTER every burst retry has passed
    // (combat entry timing differs per path), and the game's own layout can
    // re-position it later. Anchor a light self-rescheduling guard on the
    // hand node, which exists from _Ready, and resolve the holder fresh each
    // tick. SceneTreeTimer callbacks are plain delegates and run in every
    // load context, unlike Node._Process which needs script registration.
    internal static void EnsureHandGuard(Node hand)
    {
        if (hand.HasMeta(HandGuardMeta))
            return;
        hand.SetMeta(HandGuardMeta, true);
        PatchHelper.Log("[Portrait] Hand guard installed");
        ScheduleHandGuard(hand);
    }

    private static void ScheduleHandGuard(Node hand)
    {
        // Same detach rule as AssertLoop: the hand leaves the tree during
        // restore and while capstones cover the room, and the old in-tree
        // gate before the re-arm ended this "reliable heartbeat" for good
        // on the first tick that landed in such a window.
        if (Engine.GetMainLoop() is not SceneTree sceneTree || !GodotObject.IsInstanceValid(hand))
            return;
        sceneTree.CreateTimer(0.5).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(hand))
                return;
            var canvas = PortraitDisplay.CanvasSize;
            if (PortraitDisplay.IsPortrait(canvas))
            {
                // The rewards screen's own assert loop has proven flaky on
                // device; this guard re-drives that pass independently of the
                // hand's own state, since a run resumed straight into the loot
                // screen has a rewards overlay but no card holder yet.
                if (PortraitSceneCache.TopOverlay() is { } topOverlay
                    && topOverlay.GetType().Name == "NRewardsScreen")
                {
                    // The map pass hides a live loot screen while the map is
                    // shown; bring it back the moment the map is gone.
                    if (!topOverlay.Visible
                        && topOverlay.HasMeta(PortraitRewards.MapHidMeta)
                        && Engine.GetMainLoop() is SceneTree guardTree
                        && PortraitSceneCache.FindByType(guardTree.Root, "NMapScreen")
                            is not Control { Visible: true })
                    {
                        topOverlay.RemoveMeta(PortraitRewards.MapHidMeta);
                        topOverlay.Visible = true;
                        PatchHelper.Log("[Portrait] rewards screen restored after map");
                    }
                    if (topOverlay.Visible)
                    {
                        try
                        {
                            PortraitRewards.ApplyNow(topOverlay);
                        }
                        catch (Exception ex)
                        {
                            PatchHelper.Log($"[Portrait] rewards from guard failed: {ex.GetBaseException().Message}");
                        }
                    }
                }
                var holder = hand.IsInsideTree()
                    ? PortraitNodes.FindControl(hand, "CardHolderContainer")
                    : null;
                if (holder is not null)
                {
                    ApplyCapstoneHandVisibility(hand, holder);
                    // The combat intro tween slides the holder after _Ready,
                    // and it drifts on both axes: Y alone left the fan centred
                    // where the landscape scene wanted it, off to the left.
                    if (Math.Abs(holder.GlobalPosition.Y - HandBaselineY(canvas)) > 4f
                        || Math.Abs(holder.GlobalPosition.X - canvas.X * 0.5f) > 4f)
                    {
                        var before = holder.GlobalPosition;
                        PlaceHand(holder, canvas);
                        PatchHelper.Log(
                            $"[Portrait] Hand guard corrected holder {before.X:F0},{before.Y:F0} -> {holder.GlobalPosition.X:F0},{holder.GlobalPosition.Y:F0}"
                        );
                    }
                }
            }
            ScheduleHandGuard(hand);
        };
    }


    internal const float EndTurnScale = 1.18f;

    internal static Vector2 EndTurnTarget(Control button, Vector2 canvas)
    {
        var width = button.Size.X > 1f ? button.Size.X : 250f;
        return new Vector2(canvas.X - width * EndTurnScale - 38f, canvas.Y * 0.73f);
    }

    internal static void PlaceEndTurn(Control button, Vector2 canvas)
    {
        PortraitNodes.ClearAnchors(button);
        button.PivotOffset = Vector2.Zero;
        button.Scale = Vector2.One * EndTurnScale;
        button.ZAsRelative = false;
        button.ZIndex = 420;

        // Position is deliberately NOT written here any more. The button's own
        // ShowPos getter is patched to return our target, so the game's entry
        // tween glides it to the right spot; writing the position here as well
        // made the button snap mid-glide.
    }

    // The piles used to hug the bottom corners, which is exactly where the
    // outer cards of the hand fan end up, so both counters sat on top of a
    // card. They now ride the empty band above the energy and end turn row.
    private const float PileRowRatio = 0.685f;

    internal const float PileScale = 1.42f;

    internal static Vector2 PileTarget(Control pile, Vector2 canvas, bool onRight)
    {
        var width = pile.Size.X > 1f ? pile.Size.X : 86f;
        var height = pile.Size.Y > 1f ? pile.Size.Y : 86f;
        var x = onRight
            ? canvas.X - width * PileScale - PortraitHudMetrics.EdgeMargin
            : PortraitHudMetrics.EdgeMargin;
        return new Vector2(x, canvas.Y * PileRowRatio - height * PileScale * 0.5f);
    }

    internal static void PlacePile(Control pile, Vector2 canvas, bool onRight)
    {
        PortraitNodes.ClearAnchors(pile);
        pile.PivotOffset = Vector2.Zero;
        pile.Scale = Vector2.One * PileScale;
        pile.ZAsRelative = false;
        pile.ZIndex = 520;

        // AnimIn cannot be patched on device: its ORIGINAL body reads the
        // protected base field _hidePosition, and the patched copy is denied
        // that access even when the prefix itself is reflection-only (the
        // sharper form of BUG-020/BUG-022: protected base-class fields break
        // the copy too, not just methods). Same zero-flash effect from the
        // outside instead: this pass runs at CombatUi._Ready, before the
        // entry tween, and rewrites the destination the tween aims at.
        var target = PileTarget(pile, canvas, onRight);
        try
        {
            var parentTransform = (pile.GetParent() as Control)?.GetGlobalTransform()
                ?? Transform2D.Identity;
            Traverse.Create(pile).Field("_showPosition")
                .SetValue(parentTransform.AffineInverse() * target);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] pile show position rewrite failed: {ex.GetBaseException().Message}");
        }
        // Only drift beyond the tween's own motion is corrected here, so the
        // slide-in animation survives.
        if (pile.GlobalPosition.DistanceTo(target) > 220f)
            pile.Position += target - pile.GlobalPosition;
    }

    // The run HUD switches between the compact bar and the combat stack based
    // on this flag; the combat frame's lifecycle is the authoritative signal.
    internal static bool CombatHudActive { get; private set; }

    internal static void EnsureFrame(Node ui, Vector2 canvas)
    {
        // CombatUi and CombatRoom both inherit the room's landscape transform.
        // GlobalUi is the full run canvas and shares draw ordering with the HUD;
        // attaching to Window would sit behind the game's run canvas entirely.
        Node host = PortraitNodes.FindByType(ui.GetTree().Root, "NGlobalUi")
            ?? (Node)ui.GetTree().Root;
        var frame = host.GetNodeOrNull<PortraitCombatFrame>(FrameName);
        if (frame is null)
        {
            frame = new PortraitCombatFrame
            {
                Name = FrameName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZAsRelative = false,
                ZIndex = 100,
            };
            host.AddChild(frame);
            ui.TreeExiting += () =>
            {
                CombatHudActive = false;
                if (GodotObject.IsInstanceValid(frame))
                    frame.QueueFree();
                if (OperatingSystem.IsAndroid())
                {
                    AndroidGodotAppBridge.ClearStatusBarColor();
                    AndroidGodotAppBridge.HideCombatTopCover();
                }
            };
        }

        CombatHudActive = true;
        PortraitNodes.ClearAnchors(frame);
        frame.Position = Vector2.Zero;
        frame.Size = canvas;
        frame.Configure(canvas);
    }
}

[HarmonyPatch(typeof(NCombatSceneContainer), "OnWindowChange")]
internal static class CombatBackgroundWindowPatch
{
    private static void Postfix(object __instance) => PortraitCombat.ScaleBackground(__instance);
}

[HarmonyPatch(typeof(NCombatSceneContainer), "_Ready")]
internal static class CombatBackgroundReadyPatch
{
    private static void Postfix(object __instance)
    {
        var node = (Node)__instance;
        foreach (var delay in new[] { 0.1, 0.5, 1.5 })
            PortraitNodes.After(node, delay, () => PortraitCombat.ScaleBackground(__instance));
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Helpers.HandPosHelper), "GetPosition")]
internal static class HandFanPatch
{
    // Widest authored X per hand size, straight from the game's position
    // table. The postfix knows the hand size, so the fan is scaled exactly as
    // far as this hand needs instead of by a blanket factor.
    private static readonly float[] MaxFanX = { 0f, 100f, 180f, 240f, 340f, 460f, 534f, 565f, 600f, 610f };

    private static void Postfix(int handSize, ref Vector2 __result)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var widest = MaxFanX[Math.Clamp(handSize - 1, 0, MaxFanX.Length - 1)];
        __result.X = PortraitCombat.CompressFan(__result.X, widest, canvas);
    }
}

[HarmonyPatch(typeof(NPlayerHand), "_Ready")]
internal static class PlayerHandReadyPatch
{
    private static void Postfix(object __instance)
    {
        var hand = (Node)__instance;
        foreach (var delay in new[] { 0.10, 0.45, 1.2 })
            PortraitNodes.After(hand, delay, () => Apply(__instance));
    }

    private static void Apply(object instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        PortraitCombat.EnsureHandGuard((Node)instance);

        var holder = Traverse.Create(instance).Property("CardHolderContainer").GetValue<Control>();
        if (holder is null)
            return;

        PortraitCombat.PlaceHand(holder, canvas);
    }
}

internal sealed class PortraitTargetCardMonitor : Node
{
    private const double UpdateInterval = 0.03;
    private double _elapsed;

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < UpdateInterval)
            return;
        _elapsed = 0;

        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        ApplyToTree(GetTree().Root, canvas);
    }

    private static void ApplyToTree(Node root, Vector2 canvas)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is NCardPlay { Holder: not null } play)
            {
                var pointerY = play.GetViewport().GetMousePosition().Y;
                var y = Mathf.Clamp(pointerY, canvas.Y * 0.43f, canvas.Y * 0.64f);
                play.Holder.SetTargetPosition(new Vector2(canvas.X * 0.5f, y));
                play.Holder.SetTargetScale(Vector2.One * 0.98f);
                continue;
            }

            ApplyToTree(child, canvas);
        }
    }
}

internal static class PortraitSettingsOverlay
{
    private const string PreviousTopBarVisibilityMeta = "sts2_portrait_settings_topbar_visible";

    internal static void SetTopBarVisible(NSettingsScreen screen, bool settingsAreOpen)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;

        OffsetContentBelowSafeTop(screen, settingsAreOpen);

        var topBar = PortraitNodes.FindByType(screen.GetTree().Root, "NTopBar");
        if (topBar is null)
            return;

        if (settingsAreOpen)
        {
            if (!topBar.HasMeta(PreviousTopBarVisibilityMeta))
                topBar.SetMeta(PreviousTopBarVisibilityMeta, topBar.Visible);
            topBar.Visible = false;
            return;
        }

        if (topBar.HasMeta(PreviousTopBarVisibilityMeta))
        {
            topBar.Visible = (bool)topBar.GetMeta(PreviousTopBarVisibilityMeta);
            topBar.RemoveMeta(PreviousTopBarVisibilityMeta);
        }
    }

    private const string ContentOffsetMeta = "sts2_portrait_settings_offset";
    // The back tab now lives above the tab strip, so the strip and the
    // list start lower; the content scale gives back what the long General
    // tab needs to keep its last row above the nav clearance.
    private const float TabTopGap = 150f;
    private const float SettingsContentScale = 1.0f;

    // The settings screen is authored for landscape: tabs jammed at the top
    // edge, an 86-unit row pitch, the back tab half outside the canvas and a
    // dead lower half. This lays the whole screen out for a phone while it is
    // open and puts everything back on close.
    private static void OffsetContentBelowSafeTop(NSettingsScreen screen, bool open)
    {
        var tabs = PortraitNodes.FindControl(screen, "SettingsTabManager");
        var scroll = PortraitNodes.FindControl(screen, "ScrollContainer");
        if (tabs is null)
            return;

        if (open)
        {
            GrowRows(screen);
            PlaceBackButton(screen);

            if (screen.HasMeta(ContentOffsetMeta))
                return;
            var wanted = PortraitDisplay.SafeTop() + TabTopGap;
            var delta = wanted - tabs.Position.Y;
            if (delta <= 0f)
                return;
            screen.SetMeta(ContentOffsetMeta, delta);
            tabs.Position += new Vector2(0f, delta);
            if (scroll is not null)
                scroll.Position += new Vector2(0f, delta);
            return;
        }

        if (screen.HasMeta(ContentOffsetMeta))
        {
            var delta = (float)screen.GetMeta(ContentOffsetMeta);
            screen.RemoveMeta(ContentOffsetMeta);
            tabs.Position -= new Vector2(0f, delta);
            if (scroll is not null)
                scroll.Position -= new Vector2(0f, delta);
        }

        if (PortraitNodes.FindControl(screen, "Clipper") is { } clipper)
            clipper.Scale = Vector2.One;
    }

    // Rows keep their landscape pitch of ~86 units, just under thumb size.
    // Growing each row's CustomMinimumSize split the rows visually (their
    // backgrounds and content anchor differently inside the row), so the
    // whole content block is scaled instead: the Clipper is a plain Control,
    // which keeps a scale, and every row grows with its internals intact.
    private static void GrowRows(NSettingsScreen screen)
    {
        if (PortraitNodes.FindControl(screen, "Clipper") is not { } clipper)
            return;

        // The whole content block is scaled as one unit here; sweep-growing
        // individual rows inside it would fight that scale every tick.
        PortraitTouchPass.MarkManaged(clipper);

        clipper.PivotOffset = new Vector2(clipper.Size.X * 0.5f, 0f);
        clipper.Scale = Vector2.One * SettingsContentScale;
        GrowSmallControls(clipper);
        SpreadRows(clipper);
    }

    // The authored 86-unit pitch packs every row into the top half and
    // leaves the lower half of the screen dead; spreading the list fills
    // the phone's height (the scroll container absorbs any overflow).
    // The separation is derived per tab so the list ends above the back
    // tab band instead of running under it (a fixed 34 put the Credits
    // label under the tab on the General tab); short tabs get the roomier
    // pitch, long ones tighten to fit.
    // Bottom clearance for the list now that the back tab lives at the top.
    private const float BackTabBand = 80f;

    private static void SpreadRows(Node node)
    {
        if (node is BoxContainer { Vertical: true } list && list.GetChildCount() >= 6)
        {
            var rows = 0;
            var rowHeight = 0f;
            foreach (var child in list.GetChildren())
                if (child is Control { Visible: true } row && row.Size.Y > 1f)
                {
                    rows++;
                    rowHeight += row.Size.Y;
                }
            if (rows < 2)
                return;
            rowHeight /= rows;
            var scale = Math.Max(list.GetGlobalTransform().Scale.Y, 0.01f);
            var canvas = PortraitDisplay.CanvasSize;
            var floor = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom()) - BackTabBand;
            var availLocal = (floor - list.GlobalPosition.Y) / scale;
            var separation = (availLocal - rows * rowHeight) / (rows - 1);
            list.AddThemeConstantOverride("separation", (int)Mathf.Clamp(separation, 6f, 32f));
            return;
        }
        foreach (var child in node.GetChildren())
            SpreadRows(child);
    }

    private const string TickGrownMeta = "Sts2PortraitSettingsTickGrown";
    private const float TickboxScale = 1.55f;

    // The content scale brings rows to thumb pitch but the interactive bits
    // inside them (tickboxes, the value arrows) are authored ~40 units and
    // land near 12dp; grow just those in place, centered on themselves.
    private static void GrowSmallControls(Node node)
    {
        if (node is Control c && !c.HasMeta(TickGrownMeta))
        {
            // Scaling the tickbox CONTROL did nothing on device: the hover
            // reticle animation owns that transform and rewrites it every
            // frame. The inner visuals are unmanaged, so grow those: the
            // 64x64 tick square and the paginator arrow art.
            var parentType = c.GetParent()?.GetType().Name ?? "";
            if (c.Name == "TickboxVisuals"
                || (c is TextureRect && c.Name == "Image" && parentType.Contains("Arrow")))
            {
                c.SetMeta(TickGrownMeta, true);
                c.PivotOffset = c.Size * 0.5f;
                c.Scale = Vector2.One * TickboxScale;
            }
        }
        foreach (var child in node.GetChildren())
            GrowSmallControls(child);
    }

    private static void PlaceBackButton(NSettingsScreen screen)
    {
        if (PortraitNodes.FindControl(screen, "BackButton") is not { } back)
            return;

        // The settings list fills the screen to the bottom on the long
        // tabs, so a bottom-left tab landed on the Credits label. The top
        // band above the tab strip is free here (the top bar is hidden while
        // settings are open); the back tab lives there, mobile-header style.
        PortraitNodes.ClearAnchors(back);
        PortraitNodes.PlaceBackTab(
            back,
            new Vector2(PortraitHudMetrics.EdgeMargin, PortraitDisplay.SafeTop() + 6f)
        );
    }
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuOpened))]
internal static class SettingsScreenOpenedPatch
{
    private static void Postfix(NSettingsScreen __instance)
        => PortraitSettingsOverlay.SetTopBarVisible(__instance, settingsAreOpen: true);
}

[HarmonyPatch(typeof(NSettingsScreen), "OnSubmenuShown")]
internal static class SettingsScreenShownPatch
{
    private static void Postfix(NSettingsScreen __instance)
        => PortraitSettingsOverlay.SetTopBarVisible(__instance, settingsAreOpen: true);
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuClosed))]
internal static class SettingsScreenClosedPatch
{
    private static void Postfix(NSettingsScreen __instance)
        => PortraitSettingsOverlay.SetTopBarVisible(__instance, settingsAreOpen: false);
}

[HarmonyPatch(typeof(NSettingsScreen), "OnSubmenuHidden")]
internal static class SettingsScreenHiddenPatch
{
    private static void Postfix(NSettingsScreen __instance)
        => PortraitSettingsOverlay.SetTopBarVisible(__instance, settingsAreOpen: false);
}

[HarmonyPatch(typeof(NCombatUi), "Activate")]
internal static class CombatUiPatch
{
    private static void Postfix(object __instance)
    {
        var ui = (Node)__instance;
        foreach (var delay in new[] { 0.05, 0.45, 1.4 })
            PortraitNodes.After(ui, delay, () => Apply(__instance));
    }

    private static void Apply(object instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var ui = (Node)instance;
        // The 90px blue-grey strip is the renderer's clear color exposed above
        // the combat room, not a texture or Control. Match the adjacent dungeon
        // art so that uncovered cutout pixels read as a continuous background.
        RenderingServer.SetDefaultClearColor(new Color(0.024f, 0.118f, 0.078f, 1f));
        if (OperatingSystem.IsAndroid())
        {
            AndroidGodotAppBridge.SetStatusBarColor(6, 30, 20);
            AndroidGodotAppBridge.ShowCombatTopCover(6, 30, 20);
        }
        if (ui.GetNodeOrNull<PortraitTargetCardMonitor>(nameof(PortraitTargetCardMonitor)) is null)
        {
            ui.AddChild(new PortraitTargetCardMonitor { Name = nameof(PortraitTargetCardMonitor) });
        }
        PortraitCombat.EnsureFrame(ui, canvas);

        var energy = Traverse.Create(instance).Property("EnergyCounterContainer").GetValue<Control>();
        if (energy is not null)
        {
            PortraitNodes.ClearAnchors(energy);
            energy.PivotOffset = Vector2.Zero;
            energy.Scale = Vector2.One * 1.38f;
            var target = new Vector2(54f, canvas.Y * 0.72f);
            energy.Position += target - energy.GlobalPosition;
            energy.ZAsRelative = false;
            energy.ZIndex = 420;
        }

        var hand = PortraitNodes.FindControl(ui, "Hand");
        // Combat entry paths differ in when the holder exists and when the
        // hand's _Ready fires; installing the guard here too keeps the fan
        // pinned regardless of which path created this combat.
        if (hand is not null)
            PortraitCombat.EnsureHandGuard(hand);
        var holder = hand is null ? null : PortraitNodes.FindControl(hand, "CardHolderContainer");
        if (holder is not null)
            PortraitCombat.PlaceHand(holder, canvas);

        var endTurn = PortraitNodes.FindControl(ui, "EndTurnButton");
        if (endTurn is not null)
            PortraitCombat.PlaceEndTurn(endTurn, canvas);

        // Coop's ping button leaked into singleplayer loot screens, floating
        // bottom-right with nothing to ping; multiplayer does not exist on
        // this build (the menu row is gone), so it never earns its pixels.
        if (PortraitNodes.FindControl(ui, "PingButton") is { Visible: true } ping)
            ping.Visible = false;

        var draw = PortraitNodes.FindControl(ui, "DrawPile");
        var discard = PortraitNodes.FindControl(ui, "DiscardPile");
        var piles = PortraitNodes.FindControl(ui, "CombatPileContainer");
        if (piles?.GetParent() is Node pileParent)
        {
            pileParent.MoveChild(piles, pileParent.GetChildCount() - 1);
            piles.ZAsRelative = false;
            piles.ZIndex = 500;
        }
        if (draw is not null)
            PortraitCombat.PlacePile(draw, canvas, onRight: false);
        if (discard is not null)
            PortraitCombat.PlacePile(discard, canvas, onRight: true);

        // Creature containers deliberately stay untouched. Their vanilla layout
        // puts allies and enemies on the same combat plane and must remain the
        // source of truth regardless of enemy count or sprite dimensions.
    }
}

[HarmonyPatch(typeof(NEndTurnButton), "AnimIn")]
internal static class EndTurnPatch
{
    private static void Postfix(object __instance)
    {
        var button = (Control)__instance;
        // AnimIn's desktop tween lasts half a second and used to win the race,
        // putting half the button beyond the portrait viewport. Apply after it.
        foreach (var delay in new[] { 0.05, 0.62 })
            PortraitNodes.After(button, delay, () =>
        {
            var canvas = PortraitDisplay.CanvasSize;
            if (!PortraitDisplay.IsPortrait(canvas))
                return;
            PortraitCombat.PlaceEndTurn(button, canvas);
        });
    }
}

internal static class PortraitTopBar
{
    private static string _lastSignature = "";

    private static void Place(Control control, Vector2 globalPosition, float scale)
    {
        if (control is null)
            return;
        PortraitNodes.ClearAnchors(control);
        control.PivotOffset = Vector2.Zero;
        control.Scale = Vector2.One * scale;
        control.Position += globalPosition - control.GlobalPosition;
    }

    private static float PlaceFromRight(Control control, float right, float top, float scale)
    {
        if (control is null)
            return right;
        var width = control.Size.X > 1f ? control.Size.X : 70f;
        right -= width * scale;
        Place(control, new Vector2(right, top), scale);
        return right - 12f;
    }

    // Identity rules only: name or literal placeholder text. The old
    // position-based rule ("any label in the upper right") permanently hid
    // whatever legitimate control happened to pass through that region.
    private static bool IsBuildWatermarkText(Node node, string text)
        => node.Name.ToString().Contains("Build", StringComparison.OrdinalIgnoreCase)
            || text.Equals("NONE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("???", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("CI", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[NONE]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("(???)", StringComparison.OrdinalIgnoreCase);

    internal static void HideBuildWatermark(Node root, Vector2 canvas)
    {
        if (root is Label label)
        {
            if (IsBuildWatermarkText(root, label.Text ?? ""))
                label.Visible = false;
        }
        else if (root is RichTextLabel richText)
        {
            if (IsBuildWatermarkText(root, richText.Text ?? ""))
                richText.Visible = false;
        }
        else if (root.GetType().Name == "NDebugInfoLabelManager")
        {
            // The manager is a plain Node; its labels are scene-unique nodes
            // elsewhere in the owning scene (in-run text is "[ver] (date)" and
            // "MODDED (n)", which no text rule can safely match), so hide them
            // by identity through the unique-name lookup instead.
            foreach (var unique in new[] { "%ReleaseInfo", "%ModdedWarning", "%DebugSeed" })
            {
                if (root.GetNodeOrNull(unique) is CanvasItem info)
                    info.Visible = false;
            }
        }

        foreach (var child in root.GetChildren())
            HideBuildWatermark(child, canvas);
    }

    internal static void Apply(NTopBar bar)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas) || !GodotObject.IsInstanceValid(bar))
            return;

        bar.ZAsRelative = false;
        bar.ZIndex = 400;
        var safeTop = PortraitDisplay.SafeTop();
        // Fullscreen capstone screens (deck view, in-run settings) open over
        // combat too; the expanded stack would poke into their content, so
        // they always get the slim bar. The loot overlay and the map after a
        // fight count the same way: the fight is over, the potions go back to
        // their bar station (the combat scene stays alive underneath, so
        // CombatHudActive alone still says combat).
        var combat = PortraitCombat.CombatHudActive
            && !IsCapstoneScreenOpen(bar)
            && !PortraitCapstone.EclipsesCombatHud(bar);
        var left = bar.GetNodeOrNull<Control>("LeftAlignedStuff");
        var right = bar.GetNodeOrNull<Control>("RightAlignedStuff");

        var hp = PortraitSceneCache.Find(bar, "TopBarHp", "topbar:hp");
        var gold = PortraitSceneCache.Find(bar, "TopBarGold", "topbar:gold");
        var portrait = PortraitSceneCache.Find(bar, "TopBarPortrait", "topbar:portrait");
        var portraitTip = PortraitSceneCache.Find(bar, "TopBarPortraitTip", "topbar:portraitTip");
        var potions = PortraitSceneCache.Find(bar, "PotionContainer", "topbar:potions");
        var room = PortraitSceneCache.Find(bar, "RoomIcon", "topbar:room");
        var floor = PortraitSceneCache.Find(bar, "FloorIcon", "topbar:floor");
        var boss = PortraitSceneCache.Find(bar, "BossIcon", "topbar:boss");
        var map = PortraitSceneCache.Find(bar, "Map", "topbar:map");
        var deck = PortraitSceneCache.Find(bar, "Deck", "topbar:deck");
        var pause = PortraitSceneCache.Resolve2(bar);
        var timer = PortraitSceneCache.Find(bar, "TimerContainer", "topbar:timer");

        if (portrait is not null)
            portrait.Visible = false;
        if (portraitTip is not null)
            portraitTip.Visible = false;
        if (timer is not null)
            timer.Visible = false;

        var parent = bar.GetParent();
        var relics = parent is null
            ? null
            : PortraitSceneCache.Find(parent, "RelicInventory", "topbar:relics");
        if (relics is not null)
        {
            relics.ZAsRelative = false;
            relics.ZIndex = 410;
        }

        // Fullscreen capstone screens (deck view, settings) bring their own
        // chrome; the whole HUD cluster steps aside instead of bleeding
        // through their headers.
        var capstoneOpen = IsCapstoneScreenOpen(bar);
        SetVisible(hp, !capstoneOpen);
        SetVisible(gold, !capstoneOpen);
        SetVisible(potions, !capstoneOpen);
        SetVisible(map, !capstoneOpen);
        SetVisible(deck, !capstoneOpen);
        SetVisible(pause, !capstoneOpen);
        if (relics is not null)
            SetVisible(relics, !capstoneOpen);
        if (capstoneOpen)
        {
            SetVisible(room, false);
            SetVisible(floor, false);
            SetVisible(boss, false);
            SetBackdropVisible(bar, canvas, safeTop, visible: false);
            return;
        }

        if (combat)
        {
            // Combat: the expanded stack tuned in v0.3.0; the combat frame's
            // ink band owns the top of the screen, no shared backdrop. The
            // rows' transforms are reset so the per-control placement below
            // works in bar space.
            ResetRow(left);
            ResetRow(right);
            SetBackdropVisible(bar, canvas, safeTop, visible: false);
            SetVisible(room, true);
            SetVisible(floor, true);
            SetVisible(boss, true);
            var top = PortraitHudMetrics.CombatHudTop(safeTop);
            Place(hp, new Vector2(38f, top), 1.28f);
            Place(gold, new Vector2(38f, top + PortraitHudMetrics.GoldRowOffset), 1.28f);
            Place(potions, new Vector2(38f, top + PortraitHudMetrics.PotionRowOffset), 1.25f);
            Place(room, new Vector2(38f, top + PortraitHudMetrics.RoomRowOffset), 1.32f);
            Place(floor, new Vector2(168f, top + PortraitHudMetrics.RoomRowOffset), 1.32f);
            Place(boss, new Vector2(322f, top + PortraitHudMetrics.RoomRowOffset), 1.32f);

            var rightEdge = canvas.X - 38f;
            rightEdge = PlaceFromRight(pause, rightEdge, top, 1.50f);
            rightEdge = PlaceFromRight(deck, rightEdge, top, 1.50f);
            PlaceFromRight(map, rightEdge, top, 1.50f);

            PlaceRelics(relics, canvas, new Vector2(38f, top + PortraitHudMetrics.RelicRowOffset), 1.48f, canvas.X * 0.78f);
        }
        else
        {
            // Outside combat: a slim two-row bar over one shared backdrop band.
            // The rows are HBox containers whose sort re-lays children on any
            // dirty event; fighting that per child lost every time (BUG-014),
            // so the CONTAINERS are placed and their native row arrangement is
            // the design. Hidden children are skipped by the sort, which is
            // how the trinket cluster leaves row 1.
            SetBackdropVisible(bar, canvas, safeTop, visible: true);
            var top = PortraitHudMetrics.HudTop(safeTop);
            var row2 = top + PortraitHudMetrics.CompactRowPitch;

            SetVisible(room, false);
            SetVisible(floor, false);
            SetVisible(boss, false);

            PlaceRow(left, new Vector2(38f, top + 4f), 0.95f);
            // Combat pins this one as a grandchild of the row (Place writes
            // its transform directly); its margin-container slot does not
            // re-sort it on the way back to the slim bar, so hand it back
            // explicitly or it lingers at the combat coordinates (BUG-014).
            RestoreIntoSlot(room);
            // Same story for the deck button: combat places it directly at
            // 1.5x, and its DeckContainer margin slot does not re-sort on the
            // way back, so it kept a stale local position far off the bar and
            // the icon "vanished" after fights (BUG-039).
            RestoreIntoSlot(deck);
            // The map and pause buttons are direct children of the right
            // row; combat placed them directly as well, and an HBox only
            // re-lays its children on a dirty event. Hand them back and ask
            // both rows for a sort, or they stay at the combat coordinates
            // (the scroll and the gear vanished on the loot once the deck
            // alone was restored).
            RestoreIntoSlot(map);
            RestoreIntoSlot(pause);
            RestoreIntoSlot(hp);
            RestoreIntoSlot(gold);
            (right as Container)?.QueueSort();
            (left as Container)?.QueueSort();
            // Outside combat the capsule keeps its vanilla station in row 1
            // next to the gold counter, but grown into the empty stretch
            // between gold and the right icon cluster: the native slot size
            // was a fingernail with dead space beside it, and a long-press
            // tooltip had nowhere sane to anchor.
            Place(potions, new Vector2(378f, top + 2f), 1.3f);
            if (right is not null)
            {
                // Match the potion capsule right below: the naked icon strip
                // at 1.05x read as a second UI language stacked on a framed
                // plate, and their right edges did not even line up.
                const float rightScale = 1.45f;
                var width = right.Size.X > 1f ? right.Size.X : 340f;
                PlaceRow(right, new Vector2(canvas.X - 38f - width * rightScale, top), rightScale);
            }

            PlaceRelics(relics, canvas, new Vector2(38f, row2 + 10f), 1.12f, canvas.X - 68f);
        }

        // The bar and everything in it is placed by this reflow, so the global
        // touch sweep must not second-guess its sizes.
        PortraitTouchPass.MarkManaged(bar as Control);

        // Probe: the deck button vanished from the compact bar after a smith
        // visit; report its state whenever the bar's shape changes.
        var signature = $"portrait-zones-6:{canvas.X:F0}:{(combat ? "combat" : "compact")}:{relics?.GetChildCount() ?? 0}:{(deck is null ? "nodeck" : $"deck v={deck.Visible} vt={deck.IsVisibleInTree()} a={deck.Modulate.A:F2} s={deck.Scale.X:F2} p={deck.GlobalPosition.X:F0},{deck.GlobalPosition.Y:F0} sz={deck.Size.X:F0}")}";
        if (_lastSignature != signature)
        {
            _lastSignature = signature;
            // Sweep only on transitions: a full-tree walk is too expensive for
            // the perpetual reflow tick, and watermark labels only (re)appear
            // alongside scene or mode changes.
            HideBuildWatermark(bar.GetTree().Root, canvas);
            PatchHelper.Log($"[Portrait] Top bar reflow {signature}");
        }

        // The reflow ticks on every run screen, so this is the widest reach the
        // on-demand region dump can get: it answers "what is drawing here?" on
        // whatever screen is live when the trigger file appears.
        if (bar is Control barControl)
        {
            PortraitNodes.DumpRegionOnRequest(barControl);
        }
    }

    // Visible=false loses against the game's own top-bar show tweens; a
    // per-node modulate alpha survives them (the tweens animate position and
    // the bar-level modulate, not each icon's own).
    private static bool IsCapstoneScreenOpen(Node anchor) => PortraitCapstone.IsOpen(anchor);

    // Both flags together: Visible=false removes the node from container
    // sorting (and can be re-shown by game code, which the reflow undoes
    // within a tick), while the modulate alpha survives the game's own show
    // tweens as a second line of defense.
    private const string MouseFilterMeta = "Sts2PortraitMouseFilter";

    private static void SetVisible(Control control, bool visible)
    {
        if (control is null)
            return;
        if (control.Visible != visible)
            control.Visible = visible;
        var alpha = visible ? 1f : 0f;
        if (Math.Abs(control.Modulate.A - alpha) > 0.01f)
            control.Modulate = new Color(1f, 1f, 1f, alpha);
        // Never force Stop on show: RelicInventory's rect spans most of the
        // canvas (rows grow downward), and stamping Stop onto it turned the
        // whole room into a click shield (the merchant button was unreachable
        // by mouse). Park the original filter in meta while hidden and hand
        // it back untouched on show.
        if (!visible)
        {
            if (!control.HasMeta(MouseFilterMeta))
                control.SetMeta(MouseFilterMeta, (long)control.MouseFilter);
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        else if (control.HasMeta(MouseFilterMeta))
        {
            control.MouseFilter = (Control.MouseFilterEnum)(long)control.GetMeta(MouseFilterMeta);
            control.RemoveMeta(MouseFilterMeta);
        }
    }

    private static void RestoreIntoSlot(Control control)
    {
        if (control is null)
            return;
        PortraitNodes.ClearAnchors(control);
        control.PivotOffset = Vector2.Zero;
        control.Scale = Vector2.One;
        control.Position = Vector2.Zero;
    }

    private static void ResetRow(Control row)
    {
        if (row is null)
            return;
        PortraitNodes.ClearAnchors(row);
        row.PivotOffset = Vector2.Zero;
        row.Scale = Vector2.One;
        row.Position = Vector2.Zero;
    }

    private static void PlaceRow(Control row, Vector2 position, float scale)
    {
        if (row is null)
            return;
        PortraitNodes.ClearAnchors(row);
        row.PivotOffset = Vector2.Zero;
        row.Scale = Vector2.One * scale;
        row.Position += position - row.GlobalPosition;
    }

    private static void PlaceRelics(
        Control relics,
        Vector2 canvas,
        Vector2 position,
        float maxScale,
        float maxWidth
    )
    {
        if (relics is null)
            return;

        var count = 0;
        foreach (var child in relics.GetChildren())
        {
            if (child is CanvasItem { Visible: true })
                count++;
        }

        var contentWidth = Math.Max(72f, 14f + count * 68f);
        var scale = Mathf.Min(maxScale, maxWidth / contentWidth);
        relics.PivotOffset = Vector2.Zero;
        relics.Scale = Vector2.One * scale;
        relics.Position += position - relics.GlobalPosition;
    }

    private const string BackdropName = "Sts2PortraitHudBackdrop";

    // One scrim behind the compact bar, shared by every non-combat screen:
    // scrolling content passes under it and stays legible, and the punch-hole
    // area is backed the same way everywhere (combat has its own band). A
    // solid rectangle read as a black slab against the painted art, so this
    // is a deep-teal gradient, near-opaque behind the rows and fading out
    // below them.
    private static void SetBackdropVisible(NTopBar bar, Vector2 canvas, float safeTop, bool visible)
    {
        // The scrim is ~800 units deep at absolute z 390 and buried the loot
        // banner. Two writers toggling it (the reflow showing, a guard hiding)
        // made the banner flicker, so the rewards check lives here, in the
        // scrim's single owner.
        if (visible
            && PortraitSceneCache.TopOverlay() is { Visible: true } topOverlay
            && topOverlay.GetType().Name == "NRewardsScreen")
            visible = false;

        var host = bar.GetParent();
        if (host is null)
            return;

        var backdrop = host.GetNodeOrNull<TextureRect>(BackdropName);
        if (backdrop is null)
        {
            if (!visible)
                return;

            var gradient = new Gradient();
            gradient.SetColor(0, new Color(0.031f, 0.075f, 0.089f, 0.96f));
            gradient.SetColor(1, new Color(0.031f, 0.075f, 0.089f, 0f));
            gradient.AddPoint(0.62f, new Color(0.031f, 0.075f, 0.089f, 0.82f));
            var texture = new GradientTexture2D
            {
                Gradient = gradient,
                Width = 16,
                Height = 256,
                FillFrom = new Vector2(0f, 0f),
                FillTo = new Vector2(0f, 1f),
            };

            backdrop = new TextureRect
            {
                Name = BackdropName,
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZAsRelative = false,
                ZIndex = 390,
            };
            host.AddChild(backdrop);
        }

        backdrop.Visible = visible;
        if (!visible)
            return;

        PortraitNodes.ClearAnchors(backdrop);
        backdrop.Position = Vector2.Zero;
        // The fade tail extends past the rows so the hard edge disappears.
        // Deep enough to hold the event text block on the narrow canvas
        // bucket too: on 984-wide canvases the scaled prose ran past the old
        // fixed depth and its last line sat on bare art. The fade tail keeps
        // map points readable underneath as before, just deeper.
        // The event prose block grows with the readable-text floor, so a
        // fixed depth cannot hold it: measure the live block and keep the
        // gradient under its last line. Anything else on screen keeps the
        // default depth.
        // The scrim exists for bar legibility: the bar band plus a short
        // fade tail. The old default (ContentTop + 470) dated from before the
        // event prose got its own measured depth below, and it darkened the
        // top third of the map for nothing.
        var depth = PortraitHudMetrics.HudBottom(safeTop) + 140f;
        // The map draws OVER the event room without hiding it, and the deep
        // event scrim hung over the map's top third; event depth only applies
        // while the event is actually the screen being read.
        // Ancient rooms are event rooms too, but their layout has no prose
        // block; the "Title" found there belonged to the whole layout and
        // the measured depth covered the screen (every ancient read at 60
        // percent brightness). They keep the default depth.
        if (PortraitSceneCache.FindByType(bar.GetTree().Root, "NEventRoom") is { Visible: true } eventRoom
            && PortraitSceneCache.FindByType(bar.GetTree().Root, "NMapScreen")
                is not Control { Visible: true }
            && PortraitSceneCache.FindByType(bar.GetTree().Root, "NAncientEventLayout")
                is not Control { Visible: true }
            // An overlay over the event (card removal grid, rewards) is the
            // screen being read; the deep plate dimmed its top rows.
            && PortraitSceneCache.TopOverlay() is null
            && PortraitNodes.FindControl(eventRoom, "Title")?.GetParent() is Control prose
            && prose.IsVisibleInTree())
        {
            var proseBottom = prose.GetGlobalRect().Position.Y
                + prose.Size.Y * prose.GetGlobalTransform().Scale.Y;
            depth = Mathf.Max(depth, proseBottom + 60f);
        }

        backdrop.Size = new Vector2(canvas.X, depth);
    }
}


// The profile screen is authored as a 1328-wide landscape row of three cards
// with their delete buttons anchored separately at the bottom, so on a phone
// the row runs off both edges and the delete buttons no longer sit under the
// card they belong to. Fit the row to the canvas, then place each delete
// button under its own card by reading the card's real rect.
internal static class PortraitProfileScreen
{
    private const float RowWidth = 1328f;
    private const float DeleteGap = 40f;
    private const float DeleteMaxScale = 1.5f;
    private const float MessageGap = 44f;
    private const float BackButtonReserve = 150f;

    internal static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var row = PortraitNodes.FindControl(screen, "HBoxContainer");
        if (row is null)
            return;

        var authoredWidth = row.Size.X > 1f ? row.Size.X : RowWidth;
        var authoredHeight = row.Size.Y > 1f ? row.Size.Y : 560f;
        var available = canvas.X - 2f * PortraitHudMetrics.EdgeMargin;
        var scale = Mathf.Min(1f, available / authoredWidth);

        row.PivotOffset = Vector2.Zero;
        if (Mathf.Abs(row.Scale.X - scale) > 0.01f)
            row.Scale = Vector2.One * scale;

        var message = PortraitNodes.FindControl(screen, "ChooseProfileMessage");
        var messageHeight = FitMessage(message, canvas);

        // Centre the whole block in the free band instead of pinning it near
        // the top: the authored screen leaves the bottom two thirds of a phone
        // empty, which reads as a broken layout rather than a deliberate one.
        // The back tab lives at the top here like every other submenu, so the
        // reserve comes off the top of the band.
        var bandTop = PortraitDisplay.SafeTop() + BackButtonReserve;
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());
        var deleteHeight = DeleteGap + PortraitHudMetrics.MinTouchSide;
        var blockHeight = messageHeight + MessageGap + authoredHeight * scale + deleteHeight;
        var blockTop = Mathf.Max(bandTop, bandTop + (bandBottom - bandTop - blockHeight) * 0.5f);

        if (message is not null)
        {
            var messageWidth = message.Size.X > 1f ? message.Size.X : 1054f;
            var messageScale = message.Scale.X > 0.05f ? message.Scale.X : 1f;
            PlaceGlobal(
                message,
                new Vector2(
                    PortraitHudMetrics.CenterX(canvas.X, messageWidth * messageScale),
                    blockTop
                )
            );
        }

        var rowTop = blockTop + messageHeight + MessageGap;
        PortraitNodes.ClearAnchors(row);
        PlaceGlobal(
            row,
            new Vector2(PortraitHudMetrics.CenterX(canvas.X, authoredWidth * scale), rowTop)
        );

        PlaceDeleteButtons(screen);
        PlaceBackButton(screen, canvas);
    }

    private static float FitMessage(Control message, Vector2 canvas)
    {
        if (message is null)
            return 0f;

        PortraitNodes.ClearAnchors(message);
        var width = message.Size.X > 1f ? message.Size.X : 1054f;
        var height = message.Size.Y > 1f ? message.Size.Y : 97f;
        var fit = Mathf.Min(1f, (canvas.X - 2f * PortraitHudMetrics.EdgeMargin) / width);
        message.PivotOffset = Vector2.Zero;
        if (Mathf.Abs(message.Scale.X - fit) > 0.01f)
            message.Scale = Vector2.One * fit;

        return height * fit;
    }

    // Each delete button is placed from its own card's transform rather than
    // from the authored landscape offsets, which no longer line up once the
    // row has been fitted to the canvas.
    private static void PlaceDeleteButtons(Control screen)
    {
        for (var index = 1; index <= 3; index++)
        {
            var card = PortraitNodes.FindControl(screen, $"ProfileButton{index}");
            var button = PortraitNodes.FindControl(screen, $"DeleteProfileButton{index}");
            if (card is null || button is null)
                continue;

            FitInfoLabel(card);

            var width = button.Size.X > 1f ? button.Size.X : 80f;
            var height = button.Size.Y > 1f ? button.Size.Y : 80f;
            var scale = PortraitHudMetrics.TouchScale(width, height, DeleteMaxScale);
            button.PivotOffset = Vector2.Zero;
            if (Mathf.Abs(button.Scale.X - scale) > 0.01f)
                button.Scale = Vector2.One * scale;

            PortraitNodes.ClearAnchors(button);
            // Transform the card's own bottom-centre point: this survives the
            // row's scale without any arithmetic about which size is scaled.
            var bottomCentre = card.GetGlobalTransform() * new Vector2(card.Size.X * 0.5f, card.Size.Y);
            PlaceGlobal(
                button,
                new Vector2(bottomCentre.X - width * scale * 0.5f, bottomCentre.Y + DeleteGap)
            );
        }
    }

    private const string InfoFitMeta = "Sts2PortraitInfoFit";
    private const float InfoTop = 100f;
    private const float InfoHeight = 460f;

    private static void FitInfoLabel(Control card)
    {
        if (card.FindChild("Info", recursive: true, owned: false) is not Control info
            || info.HasMeta(InfoFitMeta))
            return;
        // The Info block ("Playtime / Updated <date> <time>") is a clipped
        // 340x360 rich label at y 150 in a 560-tall card; the timestamp wraps
        // to a sixth and seventh line that the rect cuts. Give the rect the
        // card's full width (the date then wraps once, not twice) and the
        // free plate above and below the block: six lines fit in 460.
        PortraitNodes.ClearAnchors(info);
        info.Position = new Vector2(0f, InfoTop);
        info.Size = new Vector2(card.Size.X, InfoHeight);
        info.SetMeta(InfoFitMeta, true);
    }

    private static void PlaceBackButton(Control screen, Vector2 canvas)
    {
        if (PortraitNodes.FindControl(screen, "BackButton") is not { } back)
            return;

        PortraitNodes.ClearAnchors(back);
        PortraitNodes.PlaceBackTab(
            back,
            new Vector2(PortraitHudMetrics.EdgeMargin, PortraitDisplay.SafeTop() + 6f)
        );
    }

    private static void PlaceGlobal(Control control, Vector2 target)
    {
        if (control.GlobalPosition.DistanceTo(target) > 1.5f)
            control.GlobalPosition = target;
    }
}

// OnSubmenuOpened is the safe hook: public, runs on every open, and its body
// touches nothing a patched copy cannot reach. Patching this screen's _Ready
// would leave its own node references null on device (see BUG-020/BUG-022).
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen.NProfileScreen), "OnSubmenuOpened")]
internal static class ProfileScreenPatch
{
    private static void Postfix(Control __instance)
        => PortraitNodes.AssertLoop(__instance, () => PortraitProfileScreen.Apply(__instance));
}


// SOURCE HOOKS. The zero-flash class of patch: instead of correcting a node
// after the game has placed it (and visibly fighting its entry tween for
// 60-120ms), these rewrite the values the game's own animations aim at, so
// the original tween delivers the node to the portrait position in one motion.
// The target methods must only touch their own fields: a patched copy is
// denied protected base-class members too (docs/BUGS.md, BUG-020/BUG-022);
// that killed an AnimIn hook on NCombatCardPile, whose rewrite now lives in
// PortraitCombat.PlacePile as plain reflection instead.

[HarmonyPatch(typeof(NEndTurnButton), "ShowPos", MethodType.Getter)]
internal static class EndTurnShowPosPatch
{
    private static void Postfix(Control __instance, ref Vector2 __result)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        // ShowPos is local to the button's parent; our target is global.
        var target = PortraitCombat.EndTurnTarget(__instance, canvas);
        var parentTransform = (__instance.GetParent() as Control)?.GetGlobalTransform()
            ?? Transform2D.Identity;
        __result = parentTransform.AffineInverse() * target;
    }
}


// Card description text sizes itself with the game's fit-to-rect autosize,
// whose ceiling is the label's MaxFontSize. Short descriptions sit AT that
// ceiling, so raising it is the one uniform lever: short texts grow, long
// ones stay fit-bound, and nothing can clip because the fit search still
// runs. This replaces the body-text floor on cards, which clipped long
// descriptions while leaving short ones tiny - the exact inconsistency it
// was supposed to prevent.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard), "UpdateVisuals")]
internal static class CardDescriptionCapPatch
{
    private const float CapFactor = 1.3f;
    private static readonly StringName CapMeta = "sts2_portrait_desc_cap";

    private static void Prefix(Node __instance)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;

        try
        {
            if (Traverse.Create(__instance).Field("_descriptionLabel").GetValue() is not Control label
                || label.HasMeta(CapMeta))
                return;

            var current = Traverse.Create(label).Property("MaxFontSize").GetValue<int>();
            if (current > 0)
            {
                Traverse.Create(label).Property("MaxFontSize")
                    .SetValue(Mathf.RoundToInt(current * CapFactor));
                label.SetMeta(CapMeta, true);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] card description cap failed: {ex.GetBaseException().Message}");
        }
    }
}

[HarmonyPatch(typeof(NContinueRunInfo), "AnimShow")]
internal static class ContinueRunInfoSourcePatch
{
    private static void Prefix(Control __instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        try
        {
            var width = __instance.Size.X > 1f ? __instance.Size.X : 520f;
            var height = __instance.Size.Y > 1f ? __instance.Size.Y : 260f;
            var globalScale = __instance.GetGlobalTransform().Scale.X;
            var anchorTop = canvas.Y * 0.27f;
            if (__instance.GetParent() is Control owner && owner.Size.Y > 1f)
                anchorTop = owner.GlobalPosition.Y - height * globalScale - 56f;

            var target = new Vector2(
                (canvas.X - width * globalScale) * 0.5f,
                Mathf.Clamp(anchorTop, PortraitDisplay.SafeTop() + 24f, canvas.Y - height * globalScale - 180f)
            );
            var parentTransform = (__instance.GetParent() as Control)?.GetGlobalTransform()
                ?? Transform2D.Identity;
            var local = parentTransform.AffineInverse() * target;

            // The tween ends at _initPosition + (0,-20); aim it so the end of
            // the game's own float-up IS the portrait spot.
            Traverse.Create(__instance).Field("_initPosition").SetValue(local + new Vector2(0f, 20f));
            __instance.ZAsRelative = false;
            __instance.ZIndex = 900;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] tooltip source hook failed: {ex.GetBaseException().Message}");
        }
    }
}

[HarmonyPatch(typeof(NContinueRunInfo), "AnimShow")]
internal static class ContinueRunInfoPatch
{
    // Roughly a fingertip plus a little air, in canvas units.
    private const float FingerClearance = 56f;

    private static void Postfix(NContinueRunInfo __instance)
    {
        foreach (var delay in new[] { 0.05, 0.4 })
            PortraitNodes.After(__instance, delay, () => Apply(__instance));
    }

    private static void Apply(Node info)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        // Move the complete 420x200 tooltip. Moving RunInfoContainer alone
        // fights its parent VBox and separates the labels from their panel.
        if (info is not Control panel)
            return;

        var width = panel.Size.X > 1f ? panel.Size.X : 520f;
        var height = panel.Size.Y > 1f ? panel.Size.Y : 260f;
        var globalScale = panel.GetGlobalTransform().X.Length();
        var maxGlobalWidth = canvas.X - 112f;
        if (width * globalScale > maxGlobalWidth)
        {
            var correction = maxGlobalWidth / (width * globalScale);
            panel.Scale *= correction;
            globalScale *= correction;
        }

        panel.ZAsRelative = false;
        panel.ZIndex = 900;

        // Position is owned by the AnimShow source hook now: it aims the
        // game's own float-up tween at the portrait spot, so writing the
        // position here mid-flight would snap the animation. This pass only
        // corrects real drift well beyond the tween's 20-unit motion.
        var anchorTop = canvas.Y * 0.27f;
        if (panel.GetParent() is Control owner && owner.Size.Y > 1f)
            anchorTop = owner.GlobalPosition.Y - height * globalScale - FingerClearance;

        var target = new Vector2(
            (canvas.X - width * globalScale) * 0.5f,
            Mathf.Clamp(
                anchorTop,
                PortraitDisplay.SafeTop() + 24f,
                canvas.Y - height * globalScale - 180f
            )
        );
        if (panel.GlobalPosition.DistanceTo(target) > 160f)
            panel.GlobalPosition = target;
    }
}

[HarmonyPatch]
internal static class PortraitFtuePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var methods = new HashSet<MethodBase>();
        foreach (var type in typeof(NFtue).Assembly.GetTypes())
        {
            if (!typeof(NFtue).IsAssignableFrom(type))
                continue;
            var method = AccessTools.DeclaredMethod(type, "_Ready");
            if (method is not null)
                methods.Add(method);
        }
        return methods;
    }

    private static void Postfix(object __instance)
    {
        var ftue = (Node)__instance;
        foreach (var delay in new[] { 0.05, 0.35, 0.9 })
            PortraitNodes.After(ftue, delay, () => Apply(ftue));
    }

    private static void Apply(Node ftue)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        // Combat Rules is not a popup panel: it is a 1336px-wide two-column
        // composition made from direct children. Treating only its Image node
        // as a panel leaves the text and arrows outside the phone viewport.
        if (ftue.GetType().Name == "NCombatRulesFtue" || ftue.Name == "CombatRulesFtue")
        {
            FitCompositeTutorial(ftue, canvas);
            return;
        }

        var popup = PortraitNodes.FindControl(ftue, "FtuePopup")
            ?? PortraitNodes.FindControl(ftue, "VerticalPopup")
            ?? PortraitNodes.FindControl(ftue, "Positioner")
            ?? FindReasonablePanel(ftue, canvas);
        if (popup is not null)
            FitPanel(popup, canvas);
        FitTutorialText(ftue, canvas.X - 150f);
    }

    private static void FitCompositeTutorial(Node ftue, Vector2 canvas)
    {
        if (ftue.HasMeta("sts2_portrait_composite_fit"))
            return;
        ftue.SetMeta("sts2_portrait_composite_fit", true);

        const float scale = 0.82f;
        var center = canvas * 0.5f;
        foreach (var child in ftue.GetChildren())
        {
            if (child is not Control control)
                continue;

            var originalPosition = control.GlobalPosition;
            control.PivotOffset = Vector2.Zero;
            control.Scale *= scale;
            control.GlobalPosition = center + (originalPosition - center) * scale;
        }
    }

    private static Control FindReasonablePanel(Node root, Vector2 canvas)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control control)
            {
                var size = control.Size;
                if (size.X > 280f && size.Y > 120f && size.X < canvas.X * 1.8f && size.Y < canvas.Y * 0.85f)
                    return control;
            }
            var nested = FindReasonablePanel(child, canvas);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static void FitPanel(Control panel, Vector2 canvas)
    {
        var width = panel.Size.X > 1f ? panel.Size.X : 720f;
        var height = panel.Size.Y > 1f ? panel.Size.Y : 460f;
        var availableHeight = canvas.Y - PortraitDisplay.SafeTop() - PortraitDisplay.SafeBottom() - 120f;
        var scale = Mathf.Min(
            1.18f,
            Mathf.Min((canvas.X - 96f) / width, availableHeight / height)
        );
        PortraitNodes.ClearAnchors(panel);
        panel.PivotOffset = Vector2.Zero;
        var currentGlobalScale = panel.GetGlobalTransform().X.Length();
        panel.Scale *= scale / Math.Max(0.001f, currentGlobalScale);
        var target = new Vector2(
            (canvas.X - width * scale) * 0.5f,
            Mathf.Max(PortraitDisplay.SafeTop() + 60f, (canvas.Y - height * scale) * 0.42f)
        );
        panel.GlobalPosition = target;
    }

    private static void FitTutorialText(Node root, float maxWidth)
    {
        if (root is Label label)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (label.Size.X > maxWidth || label.CustomMinimumSize.X > maxWidth)
                label.CustomMinimumSize = new Vector2(maxWidth, 0f);
        }
        else if (root is RichTextLabel rich)
        {
            rich.FitContent = true;
            if (rich.Size.X > maxWidth || rich.CustomMinimumSize.X > maxWidth)
                rich.CustomMinimumSize = new Vector2(maxWidth, 0f);
        }

        foreach (var child in root.GetChildren())
            FitTutorialText(child, maxWidth);
    }
}

[HarmonyPatch(typeof(NTopBar), "Initialize")]
internal static class TopBarInitializePatch
{
    private static void Postfix(NTopBar __instance)
    {
        void Start()
        {
            if (__instance.HasMeta("sts2_portrait_topbar"))
                return;

            __instance.SetMeta("sts2_portrait_topbar", true);
            Reflow(__instance, 0);
        }

        if (__instance.IsInsideTree())
            Start();
        else
            __instance.TreeEntered += Start;
    }

    private static void Reflow(NTopBar bar, int pass)
    {
        if (!GodotObject.IsInstanceValid(bar) || !bar.IsInsideTree())
            return;

        PortraitTopBar.Apply(bar);
        // The game's own top-bar layout re-runs on screen changes, so a slow
        // steady tick leaves visible drift for up to a full period. The tick
        // is cheap now (the tree sweep only runs on signature transitions),
        // so keep the steady cadence tight.
        var delay = pass < 8 ? 0.35 : 0.5;
        bar.GetTree().CreateTimer(delay).Timeout += () => Reflow(bar, pass + 1);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet), "SetAlignment")]
internal static class HoverTipAlignmentPatch
{
    // Hover tips align beside their owner, which for top-bar owners (the
    // potion capsule, relics) lands the text plate inside the pinned HUD
    // rows: the floor and boss icons printed straight over the tip title.
    // Portrait rule: a tip that would start inside the HUD stack drops to
    // just below it, clamped to the side margins.
    private static void Postfix(object __instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var text = Traverse
            .Create(__instance)
            .Field("_textHoverTipContainer")
            .GetValue<Control>();
        if (text is null || !GodotObject.IsInstanceValid(text))
            return;
        var safeTop = PortraitDisplay.SafeTop();
        var hudBottom = PortraitCombat.CombatHudActive
            ? PortraitHudMetrics.CombatHudBottom(safeTop)
            : PortraitHudMetrics.HudBottom(safeTop) + PortraitHudMetrics.RelicRowHeight + 26f;
        if (text.GlobalPosition.Y < hudBottom + 8f)
        {
            // The potion popup parks at the left edge of this same strip;
            // dropped tips start to its right so the two never stack.
            const float popupLane = 396f;
            text.GlobalPosition = new Vector2(
                Mathf.Clamp(
                    text.GlobalPosition.X,
                    popupLane,
                    canvas.X - text.Size.X - PortraitHudMetrics.EdgeMargin
                ),
                hudBottom + 8f
            );
        }
    }
}

[HarmonyPatch(typeof(NPotionPopup), "_Ready")]
internal static class PotionPopupPatch
{
    // The drink/discard popup opens hanging off the holder, which on a
    // portrait canvas shoves it past the left edge and straight into the
    // combat floor/boss row. Touch rules: park it right under the active
    // bar at the edge margin and let it read at 1.2x.
    private static void Postfix(object __instance)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var popup = (Control)__instance;
        PortraitNodes.After(popup, 0.05, () =>
        {
            var safeTop = PortraitDisplay.SafeTop();
            var top = (PortraitCombat.CombatHudActive
                ? PortraitHudMetrics.CombatHudBottom(safeTop)
                : PortraitHudMetrics.HudBottom(safeTop)) + 12f;
            const float scale = 1.2f;
            popup.PivotOffset = Vector2.Zero;
            popup.Scale = Vector2.One * scale;
            popup.GlobalPosition = new Vector2(PortraitHudMetrics.EdgeMargin, top);
        });
    }
}

[HarmonyPatch(typeof(NPotionContainer), "GrowPotionHolders")]
internal static class TopBarPotionPatch
{
    private static void Postfix(NPotionContainer __instance)
    {
        for (Node node = __instance; node is not null; node = node.GetParent())
        {
            if (node is NTopBar bar)
            {
                PortraitNodes.After(bar, 0.05, () => PortraitTopBar.Apply(bar));
                return;
            }
        }
    }
}

internal static class PortraitMap
{
    private static readonly string[] Sections = { "MapTop", "MapMid", "MapBot" };

    internal static void Cover(NMapBg background)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;

        foreach (var name in Sections)
        {
            var texture = background.GetNodeOrNull<TextureRect>(name);
            if (texture is null)
                continue;
            texture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            texture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        }

        var middle = background.GetNodeOrNull<TextureRect>("MapMid");
        if (middle is not null)
        {
            middle.CustomMinimumSize = new Vector2(0f, 3600f);
            middle.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }

        if (!background.HasMeta("sts2_portrait_overscan"))
        {
            background.SetMeta("sts2_portrait_overscan", true);
            background.Position -= new Vector2(0f, 120f);
        }
    }

    // Pinch zoom state. The graph lives in TheMap (MapBg, Paths, Points,
    // Drawings, MapMarker); scaling only Points and Paths left the player's
    // ink strokes and the position marker behind, so the whole container is
    // the zoom target. The game pans TheMap by Position, which a scale on
    // the same node leaves intact.
    private static float _zoom = 1f;
    internal const float MaxZoom = 2.2f;

    internal static float Zoom => _zoom;

    internal static NMapScreen VisibleMap()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return null;
        return PortraitSceneCache.FindByType(tree.Root, "NMapScreen") is NMapScreen
            { Visible: true } map && map.IsVisibleInTree()
            ? map
            : null;
    }

    private static IEnumerable<Control> GraphLayers(NMapScreen map)
    {
        if (PortraitNodes.FindControl(map, "Points")?.GetParent() is Control theMap)
            yield return theMap;
    }

    // A pinch scales about the fingers' midpoint. Moving PivotOffset while a
    // scale is already applied shifts the drawn graph (visual point q sits
    // at pos + q*s + pivot*(1-s)), so the position is compensated to keep
    // the graph exactly where it was when the session starts.
    internal static void BeginPinch(Vector2 globalPivot)
    {
        var map = VisibleMap();
        if (map is null)
            return;
        foreach (var layer in GraphLayers(map))
        {
            var scale = Math.Max(layer.Scale.X, 0.01f);
            var newPivot = (globalPivot - layer.GlobalPosition) / scale + layer.PivotOffset;
            var shift = (newPivot - layer.PivotOffset) * (scale - 1f);
            layer.PivotOffset = newPivot;
            layer.Position += shift;
        }
    }

    internal static void SetZoom(float zoom)
    {
        var map = VisibleMap();
        if (map is null)
            return;
        zoom = Mathf.Clamp(zoom, 1f, MaxZoom);
        _zoom = zoom;
        foreach (var layer in GraphLayers(map))
        {
            layer.Scale = Vector2.One * zoom;
        }
    }

    // Two-finger drag while zoomed pans horizontally. The game rewrites
    // TheMap.Position every frame for its own vertical scroll, so a Position
    // write is undone instantly; PivotOffset is untouched by the game, and
    // with scale s a pivot shift dp moves the drawn graph by dp*(1-s). At
    // zoom 1 the factor is zero, so the pan needs no reset.
    internal static void PanX(float deltaX)
    {
        if (_zoom <= 1.001f)
            return;
        var map = VisibleMap();
        if (map is null)
            return;
        foreach (var layer in GraphLayers(map))
            layer.PivotOffset += new Vector2(deltaX / (1f - _zoom), 0f);
    }

    internal static void CenterGraph(NMapScreen screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        var points = PortraitNodes.FindControl(screen, "Points");
        var paths = PortraitNodes.FindControl(screen, "Paths");
        if (points is null)
            return;

        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var child in points.GetChildren())
        {
            if (child is not Control control || control.Name.ToString().Contains("Vote"))
                continue;
            min = Mathf.Min(min, control.Position.X);
            max = Mathf.Max(max, control.Position.X + control.Size.X);
        }

        if (min > max)
            return;
        var offset = (canvas.X - (max - min)) * 0.5f - min;
        points.Position = new Vector2(offset, points.Position.Y);
        if (paths is not null)
            paths.Position = new Vector2(offset, paths.Position.Y);
    }
}

[HarmonyPatch(typeof(NMapBg), "_Ready")]
internal static class MapBackgroundReadyPatch
{
    private static void Postfix(NMapBg __instance) => PortraitMap.Cover(__instance);
}

[HarmonyPatch(typeof(NMapBg), "OnWindowChange")]
internal static class MapBackgroundWindowPatch
{
    private static void Postfix(NMapBg __instance) => PortraitMap.Cover(__instance);
}

[HarmonyPatch(typeof(NMapScreen), "_Ready")]
internal static class MapScreenReadyPatch
{
    private const string FillName = "Sts2PortraitMapFill";

    private static void Postfix(NMapScreen __instance)
    {
        PortraitNodes.After(__instance, 0.15, () =>
        {
            if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
                return;

            if (__instance.GetNodeOrNull(FillName) is null)
            {
                var fill = new ColorRect
                {
                    Name = FillName,
                    Color = new Color("6d5637"),
                    AnchorRight = 1f,
                    AnchorBottom = 1f,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                __instance.AddChild(fill);
                __instance.MoveChild(fill, 0);
            }
            PortraitMap.CenterGraph(__instance);
        });
        // Touch rule: the drawing tools ship at 60x60, under the touch
        // minimum, and spawn after _Ready; lift them on the assert loop and
        // keep the row anchored above the gesture strip.
        PortraitNodes.AssertLoop(__instance, () =>
        {
            var canvas = PortraitDisplay.CanvasSize;
            if (!PortraitDisplay.IsPortrait(canvas))
                return;

            // The map instance stays in the tree, hidden, between visits and
            // this loop keeps ticking on it. Everything below assumes the map
            // is the screen actually being shown; without this gate the
            // rewards cleanup once fired during a card pick and hid the live
            // rewards screen under it.
            if (!__instance.Visible || !__instance.IsVisibleInTree())
                return;

            PortraitNodes.DumpRegionOnRequest(__instance);

            // A rewards screen visible under the map is either the finished
            // screen the overlay stack forgot to hide (its Proceed arrow
            // floated over the map), or a LIVE loot screen with the map
            // toggled on top of it. Hide it while the map shows, but mark it
            // so the hand guard can restore it when the map closes; hiding
            // without the mark left an invisible live loot screen behind a
            // seemingly empty room.
            if (PortraitSceneCache.FindByType(__instance.GetTree().Root, "NRewardsScreen")
                    is { Visible: true } rewards)
            {
                rewards.SetMeta(PortraitRewards.MapHidMeta, true);
                rewards.Visible = false;
                PatchHelper.Log("[Portrait] rewards screen hidden while map shows");
            }

            // Settings and pause are capstones drawn UNDER the map's own
            // furniture: the legend and the drawing-tools plate stayed bright
            // over the settings rows. Hide both while a capstone is open and
            // hand them back on close (meta marks what this pass hid).
            var capstoneUp = PortraitCapstone.IsOpen(__instance);
            foreach (var name in new[] { "MapLegend", "DrawingTools" })
            {
                if (PortraitNodes.FindControl(__instance, name) is not { } furniture)
                    continue;
                const string hiddenMeta = "Sts2PortraitMapFurnitureHidden";
                if (capstoneUp && furniture.Visible)
                {
                    furniture.Visible = false;
                    furniture.SetMeta(hiddenMeta, true);
                }
                else if (!capstoneUp && furniture.HasMeta(hiddenMeta))
                {
                    furniture.RemoveMeta(hiddenMeta);
                    furniture.Visible = true;
                }
            }
            if (capstoneUp)
                return;

            // The legend floated mid-screen over live map nodes; the lower
            // band of the portrait map is empty, so it belongs there.
            if (PortraitNodes.FindControl(__instance, "MapLegend") is { } legend)
            {
                var legendHeight = legend.Size.Y > 1f ? legend.Size.Y : 454f;
                var legendTarget = new Vector2(
                    canvas.X - (legend.Size.X > 1f ? legend.Size.X : 340f) - PortraitHudMetrics.EdgeMargin,
                    PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom()) - legendHeight - 130f
                );
                if (legend.GlobalPosition.DistanceTo(legendTarget) > 3f)
                    legend.GlobalPosition = legendTarget;
            }

            var safeBottom = PortraitDisplay.SafeBottom();

            // The quill and eraser live INSIDE the DrawingTools plate; moving
            // and scaling them one by one tore them off their own backdrop
            // (the quill ended up half on bare parchment). The plate moves as
            // one unit and its children ride along; only Clear, which is a
            // sibling, is placed after it.
            // Everything, Clear included, lives inside the plate's own
            // HBoxContainer; placing any child individually tears the box
            // apart (the first attempt moved Clear out of formation and the
            // remaining icons vanished with the re-sort). The plate is the
            // only node this pass may touch: scale and place it as one unit
            // and its children lay themselves out.
            if (PortraitNodes.FindControl(__instance, "DrawingTools") is { } plate)
            {
                const float plateScale = 1.8f;
                var plateW = plate.Size.X > 1f ? plate.Size.X : 208f;
                var plateH = plate.Size.Y > 1f ? plate.Size.Y : 68f;
                plate.PivotOffset = Vector2.Zero;
                if (Mathf.Abs(plate.Scale.X - plateScale) > 0.01f)
                    plate.Scale = Vector2.One * plateScale;
                var plateTarget = new Vector2(
                    PortraitHudMetrics.EdgeMargin,
                    PortraitHudMetrics.BottomAnchoredY(canvas.Y, safeBottom, plateH * plateScale)
                );
                if (plate.GlobalPosition.DistanceTo(plateTarget) > 1.5f)
                    plate.GlobalPosition = plateTarget;
            }
        });
        PortraitNodes.After(__instance, 0.8, () => PortraitMap.CenterGraph(__instance));
    }
}

[HarmonyPatch(typeof(NMapScreen), "MapLegendX", MethodType.Getter)]
internal static class MapLegendPatch
{
    private static void Postfix(NMapScreen __instance, ref float __result)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;
        __result = Math.Min(__result, __instance.Size.X - 360f);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom), "_Ready")]
internal static class EventRoomPatch
{
    private static void Postfix(object __instance)
    {
        var room = (Node)__instance;
        PortraitNodes.AssertLoop(room, () =>
        {
            var canvas = PortraitDisplay.CanvasSize;
            if (!PortraitDisplay.IsPortrait(canvas))
                return;

            var layout = PortraitNodes.FindControl(room, "DefaultEventLayout");
            var title = PortraitNodes.FindControl(room, "Title");
            if (layout is null || title?.GetParent() is not Control block)
                return;

            // Portrait composition instead of overlap-avoidance: the text
            // block sits right under the bar, the options anchor to the thumb
            // zone at the bottom, and the event art fills the band between
            // them. The authored layout stacked all of it in the top third of
            // a landscape canvas and left the rest empty.
            layout.Position = new Vector2(layout.Position.X, 0f);

            var safeTop = PortraitDisplay.SafeTop();
            var contentTop = PortraitHudMetrics.ContentTop(safeTop);
            var options = PortraitNodes.FindControl(layout, "OptionsContainer");

            // The options live inside the text VBox; the container's sort
            // would keep pulling them back up, so they move out once.
            if (options is not null && options.GetParent() != layout)
            {
                var keep = options.GlobalPosition;
                options.GetParent().RemoveChild(options);
                layout.AddChild(options);
                options.GlobalPosition = keep;
            }

            // Text size is owned by the global readable-text floor now; the
            // old 1.1 block scale stacked on top of it and pushed the last
            // line off the scrim.
            const float textScale = 1.0f;
            block.PivotOffset = Vector2.Zero;
            block.Scale = Vector2.One * textScale;
            var blockWidth = (block.Size.X > 1f ? block.Size.X : 800f) * textScale;
            block.GlobalPosition = new Vector2((canvas.X - blockWidth) * 0.5f, contentTop);
            // The shared scrim draws at Z 390 over room content (so map
            // points scroll under it); event prose must read ON the scrim,
            // not under its dark end. But a capstone (the upgrade/deck grid
            // an option opens) lives in normal layering, and the pinned prose
            // drew straight over its cards; hand the Z back while one is up.
            // The event layout lives in the room container, never on the
            // overlay stack, so ANYTHING on that stack (the upgrade grid an
            // option opens, card picks, rewards) draws above the event and
            // the pinned prose must yield to it.
            if (PortraitCapstone.IsOpen(layout)
                || PortraitSceneCache.TopOverlay() is { Visible: true }
                || PortraitSceneCache.FindByType(layout.GetTree().Root, "NMapScreen")
                    is Control { Visible: true })
            {
                block.ZAsRelative = true;
                block.ZIndex = 0;
                return;
            }
            block.ZAsRelative = false;
            block.ZIndex = 395;

            var optionsTop = canvas.Y;
            if (options is not null)
            {
                const float optionsScale = 1.18f;
                // Two-line option plates overhang their authored row height
                // and the rows drew on top of each other; open the list up.
                if (options is BoxContainer { Vertical: true } optionList)
                    optionList.AddThemeConstantOverride("separation", 30);
                // Regular event options carry the same 830x74 unwrapped
                // Text label as the ancients' (BUG-047): "Remove 2 cards
                // from your" was cut at the plate's edge.
                PortraitAncientEvent.FitOptionText(options);
                options.PivotOffset = Vector2.Zero;
                options.Scale = Vector2.One * optionsScale;
                var optionsWidth = (options.Size.X > 1f ? options.Size.X : 800f) * optionsScale;
                // The VBox keeps its old Size for a frame or more after rows
                // leave (three plates became one "Proceed"), so the block
                // was hung as if three rows were still there and the lone
                // plate floated mid-screen. Measure the visible rows.
                var visibleRows = 0;
                var rowsHeight = 0f;
                foreach (var child in options.GetChildren())
                {
                    if (child is not Control { Visible: true } row)
                        continue;
                    rowsHeight += Math.Max(row.Size.Y, row.CustomMinimumSize.Y);
                    visibleRows++;
                }
                if (visibleRows > 1)
                    rowsHeight += 30f * (visibleRows - 1);
                var optionsHeight = (rowsHeight > 1f ? rowsHeight : (options.Size.Y > 1f ? options.Size.Y : 220f)) * optionsScale;
                optionsTop = canvas.Y - PortraitDisplay.SafeBottom() - optionsHeight - 90f;
                options.GlobalPosition = new Vector2((canvas.X - optionsWidth) * 0.5f, optionsTop);
            }

            // Event art: fill the free band and crop with a rule-of-thirds
            // bias. The portraits are wide (~2560x1200) and paint the subject
            // left of center (the landscape screen puts prose on the right),
            // so a straight center crop routinely cuts the subject in half;
            // anchor the visible window around the 38% mark instead, clamped
            // so it never slides past the texture edges.
            if (PortraitNodes.FindControl(layout, "Portrait") is { } art)
            {
                var bandTop = contentTop + 470f;
                var bandBottom = optionsTop - 40f;
                var artBaseHeight = art.Size.Y > 1f ? art.Size.Y : 1200f;
                var artBaseWidth = art.Size.X > 1f ? art.Size.X : 2560f;
                var scale = Mathf.Clamp((bandBottom - bandTop) / artBaseHeight, 0.7f, 1.35f);
                var renderedWidth = artBaseWidth * scale;
                const float focus = 0.38f;
                var artX = renderedWidth <= canvas.X
                    ? (canvas.X - renderedWidth) * 0.5f
                    : Mathf.Clamp(canvas.X * 0.5f - renderedWidth * focus, canvas.X - renderedWidth, 0f);
                art.PivotOffset = Vector2.Zero;
                art.Scale = Vector2.One * scale;
                art.GlobalPosition = new Vector2(
                    artX,
                    bandTop + (bandBottom - bandTop - artBaseHeight * scale) * 0.5f
                );
            }
        });
    }
}

internal static class PortraitShop
{
    internal static void Apply(Node inventory)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var slots = PortraitNodes.FindControl(inventory, "SlotsContainer");
        if (slots is null)
            return;

        const float margin = 24f;
        var top = Math.Max(210f, PortraitDisplay.SafeTop() + 180f);
        var bottom = 170f + PortraitDisplay.SafeBottom();
        var panelWidth = canvas.X - margin * 2f;
        var panelHeight = canvas.Y - top - bottom;

        if (slots is TextureRect texture)
        {
            texture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            texture.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
        PortraitNodes.ClearAnchors(slots);
        slots.PivotOffset = Vector2.Zero;
        slots.Scale = Vector2.One;
        slots.Position = new Vector2(margin, top);
        slots.Size = new Vector2(panelWidth, panelHeight);

        var cards = new List<Control>();
        PortraitNodes.CollectByType(slots, "NMerchantCard", cards);
        cards.RemoveAll(card => !card.Visible);
        var removal = PortraitNodes.FindByType(slots, "NMerchantCardRemoval");
        var hasRemoval = removal is { Visible: true };
        var itemCount = cards.Count + (hasRemoval ? 1 : 0);
        const int columns = 2;
        var rows = Math.Max(1, (itemCount + columns - 1) / columns);
        // The relic and potion shelves need thumb-sized icons (they were
        // ~15dp at 0.8x in a 190-unit band); the band grows and the cards
        // above give up a little height through the cell clamp.
        const float utilityBand = 520f;
        var cellWidth = panelWidth / columns;
        var cellHeight = (panelHeight - utilityBand) / rows;
        var scale = Mathf.Clamp(cellHeight / 500f, 0.72f, 1.05f);
        var origin = slots.GlobalPosition;

        for (var i = 0; i < cards.Count; i++)
            Place(cards[i], origin + CellCenter(i, cellWidth, cellHeight), scale);
        if (hasRemoval)
            Place(removal, origin + CellCenter(cards.Count, cellWidth, cellHeight), scale);

        // The utility band holds two shelves side by side, each three slots
        // wide; 1.4x lands the icons near 30dp with prices still inside the
        // rug, shelves parked at the band's left and right thirds.
        var relics = PortraitNodes.FindControl(slots, "Relics");
        var potions = PortraitNodes.FindControl(slots, "Potions");
        // Two thumb-sized shelves do not fit side by side on this canvas
        // (they collided and the last potion fell off the rug), so they
        // stack: relics above, potions below, both centered and clear of
        // the back tab and of the last card row's price tags.
        const float shelfScale = 1.4f;
        const float shelfPitch = 175f;
        var bandY = panelHeight - utilityBand + 110f;
        // The shelf containers report less width than their three slots
        // draw (measured ~370 units at 1x), so the rendered width centers.
        var relicsWidth = Math.Max(relics?.Size.X ?? 0f, 370f) * shelfScale;
        var potionsWidth = Math.Max(potions?.Size.X ?? 0f, 370f) * shelfScale;
        if (relics is not null)
            Place(relics, origin + new Vector2((panelWidth - relicsWidth) * 0.5f, bandY), shelfScale);
        if (potions is not null)
            Place(potions, origin + new Vector2((panelWidth - potionsWidth) * 0.5f, bandY + shelfPitch), shelfScale);
    }

    private static Vector2 CellCenter(int index, float width, float height)
        => new(
            index % 2 * width + width * 0.5f,
            index / 2 * height + height * 0.5f
        );

    private static void Place(Control control, Vector2 globalPosition, float scale)
    {
        control.PivotOffset = Vector2.Zero;
        control.Scale = Vector2.One * scale;
        control.Position += globalPosition - control.GlobalPosition;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory), "Open")]
internal static class MerchantOpenPatch
{
    private static void Postfix(object __instance)
    {
        var inventory = (Node)__instance;
        inventory.RemoveMeta("sts2_portrait_shop_closed");
        if (inventory.HasMeta("sts2_portrait_shop_loop"))
            return;
        inventory.SetMeta("sts2_portrait_shop_loop", true);
        Reflow(inventory);
    }

    private static void Reflow(Node inventory)
    {
        if (!GodotObject.IsInstanceValid(inventory) || !inventory.IsInsideTree())
            return;
        if (inventory.HasMeta("sts2_portrait_shop_closed"))
        {
            // Stop the chain when the shop closes; Open restarts it on the
            // next visit instead of letting the timer idle for the whole run.
            inventory.RemoveMeta("sts2_portrait_shop_loop");
            return;
        }
        PortraitShop.Apply(inventory);
        inventory.GetTree().CreateTimer(0.5).Timeout += () => Reflow(inventory);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory), "Close")]
internal static class MerchantClosePatch
{
    private static void Prefix(object __instance)
        => ((Node)__instance).SetMeta("sts2_portrait_shop_closed", true);
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "_Ready")]
internal static class MerchantRoomPatch
{
    private static void Postfix(object __instance)
    {
        var room = (Node)__instance;
        PortraitNodes.AssertLoop(room, () =>
        {
            var background = PortraitNodes.FindControl(room, "BgContainer");
            if (background is null || !PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
                return;
            background.PivotOffset = background.Size * 0.5f;
            background.Scale = Vector2.One * 1.75f;
        });
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom), "_Ready")]
internal static class RestSitePatch
{
    private static void Postfix(object __instance)
    {
        var room = (Node)__instance;
        PortraitNodes.AssertLoop(room, () =>
        {
            if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
                return;
            var background = PortraitNodes.FindControl(room, "BgContainer");
            if (background is not null)
            {
                background.PivotOffset = background.Size * 0.5f;
                background.Scale = Vector2.One * 1.72f;
            }
            PortraitRestSite.EnsureLoop(room);
        });
    }
}

internal static class PortraitRestSite
{
    private const string LoopMeta = "Sts2PortraitRestLoop";

    internal static void EnsureLoop(Node room)
    {
        if (room is null || !GodotObject.IsInstanceValid(room) || room.HasMeta(LoopMeta))
            return;
        room.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(room, () => Apply(room));
    }

    // The campfire choices are authored for a landscape center: prompt and
    // two cards floating high above the character. Touch rules: the card
    // row grows and drops into the thumb zone (the seated character stays
    // visible above it), the prompt rides right above the row, and the
    // hover description text sits above the prompt.
    private static void Apply(Node room)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var choices = PortraitNodes.FindControl(room, "ChoicesContainer");
        if (choices is null)
            return;

        var safeBottom = PortraitDisplay.SafeBottom();
        var baseW = choices.Size.X > 1f ? choices.Size.X : 799f;
        var baseH = choices.Size.Y > 1f ? choices.Size.Y : 163f;
        var scale = PortraitHudMetrics.FillScale(
            baseW,
            baseH,
            canvas.X - PortraitHudMetrics.EdgeMargin * 2f,
            280f,
            1.4f
        );
        var rowHeight = baseH * scale;
        var rowY = PortraitHudMetrics.BottomAnchoredY(canvas.Y, safeBottom, rowHeight) - 40f;
        PortraitNodes.ClearAnchors(choices);
        choices.PivotOffset = Vector2.Zero;
        choices.Scale = Vector2.One * scale;
        choices.GlobalPosition = new Vector2(
            PortraitHudMetrics.CenterX(canvas.X, baseW * scale),
            rowY
        );

        var headerY = rowY - 110f;
        if (choices.GetParent() is Control screen)
        {
            if (PortraitNodes.FindControl(screen, "Header") is { } header)
            {
                const float headerScale = 1.2f;
                var headerWidth = (header.Size.X > 1f ? header.Size.X : 1000f) * headerScale;
                header.PivotOffset = Vector2.Zero;
                header.Scale = Vector2.One * headerScale;
                header.GlobalPosition = new Vector2(
                    PortraitHudMetrics.CenterX(canvas.X, headerWidth),
                    headerY
                );
            }
            if (PortraitNodes.FindControl(screen, "Description") is { } description)
            {
                var descHeight = description.Size.Y > 1f ? description.Size.Y : 393f;
                description.GlobalPosition = new Vector2(
                    description.GlobalPosition.X,
                    headerY - 24f - descHeight
                );
            }
        }
    }
}

// _Ready is unpatchable here: its body reads the protected base field _cards
// (BUG-031). AfterCapstoneOpened is the safe hook: public virtual on the base,
// not overridden by the deck screen, body touches only its own private field,
// and the capstone container calls it right after the screen opens. Bonus:
// every NCardsViewScreen subclass with a sort row gets the same fit.
// The grid screens (deck view, smith/remove/transform picks) park their
// footer at the canvas edge: back tab, the View Upgrades box and the one-line
// caption hugged the gesture strip while the confirm tick sat higher up. The
// footer is one strip 60 above the content bottom: back tab left, the tickbox
// beside it at the same center line, the caption centered above them at
// 1.5x, the confirm tick right on the same baseline. Everything derives from
// the content bottom, so shorter phones keep the same strip.
internal static class PortraitGridStrip
{
    private const float StripInset = 60f;
    private const float TabHeight = 110f;
    private const float TickboxScale = 2.2f;
    private const float CaptionScale = 1.5f;
    private const float CaptionReserve = 44f;
    private const string PlateName = "Sts2PortraitGridStripPlate";

    internal static void Place(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var baseline = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom()) - StripInset;
        var tabTop = baseline - TabHeight;

        var back = PortraitNodes.FindControl(screen, "Close") ?? PortraitNodes.FindControl(screen, "BackButton");
        if (back is not null)
            PortraitNodes.PlaceBackTab(back, new Vector2(PortraitHudMetrics.EdgeMargin, tabTop));

        var nextX = PortraitHudMetrics.EdgeMargin + 250f;
        if (PortraitNodes.FindControl(screen, "Upgrades") is { Visible: true } box && box.Size.X > 1f)
        {
            box.PivotOffset = Vector2.Zero;
            if (Math.Abs(box.Scale.X - TickboxScale) > 0.01f)
                box.Scale = Vector2.One * TickboxScale;
            var h = box.Size.Y * TickboxScale;
            var target = new Vector2(nextX, tabTop + (TabHeight - h) * 0.5f);
            if (box.GlobalPosition.DistanceTo(target) > 1.5f)
                box.GlobalPosition = target;
        }

        if (PortraitNodes.FindControl(screen, "BottomLabel") is { } label && label.Size.X > 1f)
        {
            // The label sits in a container that re-lays it on every sort,
            // so the container is what moves (BUG-014 rule); the label
            // itself only when it stands alone.
            var caption = label.GetParent() is Container parent ? parent : label;
            PortraitNodes.ClearAnchors(caption);
            caption.PivotOffset = Vector2.Zero;
            if (Math.Abs(caption.Scale.X - CaptionScale) > 0.01f)
                caption.Scale = Vector2.One * CaptionScale;
            var w = caption.Size.X * CaptionScale;
            var h = caption.Size.Y * CaptionScale;
            var target = new Vector2((canvas.X - w) * 0.5f, tabTop - 36f - h);
            if (caption.GlobalPosition.DistanceTo(target) > 1.5f)
                caption.GlobalPosition = target;
        }

        if (PortraitNodes.FindControl(screen, "Confirm") is { } confirm && confirm.Size.X > 1f)
        {
            var rect = confirm.GetGlobalRect();
            var target = new Vector2(canvas.X - rect.Size.X - PortraitHudMetrics.EdgeMargin, baseline - rect.Size.Y);
            if (rect.Position.DistanceTo(target) > 1.5f)
                confirm.GlobalPosition += target - rect.Position;
        }

        // The strip needs its own ground: cards scrolled under it made the
        // caption unreadable. A dark plate sits right above the grid in tree
        // order (under the footer controls), and the grid's viewport ends at
        // the plate so the last row can still scroll clear of it.
        var stripTop = tabTop - 36f - CaptionReserve - 16f;
        if (PortraitNodes.FindControl(screen, "CardGrid") is { } grid)
        {
            var plate = screen.GetNodeOrNull<ColorRect>(PlateName);
            if (plate is null)
            {
                plate = new ColorRect
                {
                    Name = PlateName,
                    Color = new Color(0.03f, 0.05f, 0.07f, 0.82f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                screen.AddChild(plate);
                if (grid.GetParent() == screen)
                    screen.MoveChild(plate, grid.GetIndex() + 1);
            }
            var plateRect = new Rect2(0f, stripTop, canvas.X, canvas.Y - stripTop);
            if (plate.GlobalPosition.DistanceTo(plateRect.Position) > 0.5f || plate.Size.DistanceTo(plateRect.Size) > 0.5f)
            {
                plate.GlobalPosition = plateRect.Position;
                plate.Size = plateRect.Size;
            }
            var gridRect = grid.GetGlobalRect();
            var wantBottom = stripTop - 8f;
            if (Math.Abs(gridRect.End.Y - wantBottom) > 1f)
            {
                if (grid.AnchorBottom > 0.5f)
                    grid.OffsetBottom += wantBottom - gridRect.End.Y;
                else
                    grid.Size = new Vector2(grid.Size.X, Math.Max(200f, wantBottom - gridRect.Position.Y));
            }
        }
    }
}

[HarmonyPatch(
    typeof(MegaCrit.Sts2.Core.Nodes.Screens.NCardsViewScreen),
    "AfterCapstoneOpened"
)]
internal static class DeckViewSortRowPatch
{
    // The sort row is a 1150-wide hbox centered inside a 545-wide holder;
    // on narrow canvases its first tab started off-screen ("Obtained" read
    // as "ed"). The holder is a plain Control, so a fitted scale sticks.
    private static void Postfix(object __instance)
    {
        var screen = (Control)__instance;
        PortraitNodes.AssertLoop(screen, () =>
        {
            var canvas = PortraitDisplay.CanvasSize;
            if (!PortraitDisplay.IsPortrait(canvas))
                return;
            PortraitGridStrip.Place(screen);
            if (PortraitNodes.FindControl(screen, "SortingOptions") is not { } holder)
                return;
            if (holder.GetNodeOrNull<Control>("HBoxContainer") is not { } row)
                return;
            var rowWidth = row.Size.X > 1f ? row.Size.X : 1150f;
            var fit = Math.Min(1f, (canvas.X - 2f * PortraitHudMetrics.EdgeMargin) / rowWidth);
            row.PivotOffset = Vector2.Zero;
            row.Scale = Vector2.One * fit;
            // The bar is hidden under this capstone, so the row takes the
            // top band from the safe inset (it sat inside the cutout band),
            // and the grid's first row starts under it.
            var rowTop = PortraitDisplay.SafeTop() + 16f;
            row.GlobalPosition = new Vector2(
                PortraitHudMetrics.CenterX(canvas.X, rowWidth * fit),
                rowTop
            );
            if (!screen.HasMeta("Sts2PortraitDeckGridOffset")
                && PortraitNodes.FindControl(screen, "CardGrid") is { } grid)
            {
                screen.SetMeta("Sts2PortraitDeckGridOffset", true);
                try
                {
                    var rowHeight = (row.Size.Y > 1f ? row.Size.Y : 60f) * fit;
                    var contentTop = rowTop + rowHeight + 40f;
                    var firstRowTop = grid.GlobalPosition.Y + 80f;
                    var current = (int)(Traverse.Create(grid).Property("YOffset").GetValue() ?? 0);
                    var shift = (int)(contentTop - firstRowTop);
                    Traverse.Create(grid).Property("YOffset").SetValue(current + shift);
                    AccessTools.Method(grid.GetType(), "ReflowColumns")?.Invoke(grid, null);
                }
                catch (Exception ex)
                {
                    PatchHelper.Log($"[Portrait] deck grid offset failed: {ex.GetBaseException().Message}");
                }
            }
            // The View Upgrades tickbox lives in the shared footer strip
            // (PortraitGridStrip) together with the caption and the tab.
        });
    }
}

internal static class PortraitModding
{
    private const string LoopMeta = "Sts2PortraitModdingLoop";

    internal static void EnsureLoop(Control screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || screen.HasMeta(LoopMeta))
            return;
        screen.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(screen, () => Apply(screen));
    }

    // Entry point for the hand guard, which re-drives this pass because the
    // screen's own loop has died silently on device more than once.
    internal static void ApplyNow(Control screen) => Apply(screen);

    // Landscape puts the mod list and the detail panel side by side, which
    // pushes the detail half off a portrait canvas; stack them instead.
    private static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var border = PortraitNodes.FindControl(screen, "ModsBorder");
        var info = PortraitNodes.FindControl(screen, "ModInfoContainer");
        if (border is null || info is null)
            return;

        var top = PortraitDisplay.SafeTop() + 80f;
        PortraitNodes.ClearAnchors(border);
        border.GlobalPosition = new Vector2(
            PortraitHudMetrics.CenterX(canvas.X, border.Size.X),
            top
        );
        PortraitNodes.ClearAnchors(info);
        info.GlobalPosition = new Vector2(
            PortraitHudMetrics.CenterX(canvas.X, info.Size.X),
            top + border.Size.Y + 28f
        );
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen.NModdingScreen), "_Ready")]
internal static class ModdingScreenPatch
{
    private static void Postfix(object __instance)
    {
        var screen = (Control)__instance;
        PortraitModding.EnsureLoop(screen);
    }
}

internal static class PortraitCompendium
{
    private const string LoopMeta = "Sts2PortraitCompendiumLoop";

    internal static void EnsureLoop(Control submenu)
    {
        if (submenu is null || !GodotObject.IsInstanceValid(submenu) || submenu.HasMeta(LoopMeta))
            return;
        submenu.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(submenu, () => Apply(submenu));
    }

    // The compendium's margin container keeps its landscape footprint
    // (1920x1080 centered on the canvas), which pushes the right-aligned
    // bottom row's visible button past the canvas edge. Sizing the margin
    // box to the canvas pulls everything inside; the rows keep their own
    // container arrangement.
    private static void Apply(Control submenu)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var margin = PortraitNodes.FindControl(submenu, "MarginContainer");
        var content = PortraitNodes.FindControl(submenu, "VBoxContainer");
        if (margin is null || content is null)
            return;

        var safeTop = PortraitDisplay.SafeTop();
        var safeBottom = PortraitDisplay.SafeBottom();
        var bandTop = PortraitHudMetrics.ContentTop(safeTop);
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, safeBottom);

        // Containers reset child scale on every sort, so no scale survives
        // inside the margin/vbox chain. The shelf cards move OUT to the
        // submenu root (a plain Control) once and get laid out by hand:
        // three cards in a row scaled to the canvas, the stats chip
        // centered under them. The emptied stock furniture hides.
        margin.Visible = false;

        var shelf = new[]
        {
            PortraitNodes.FindControl(submenu, "CardLibraryButton"),
            PortraitNodes.FindControl(submenu, "RelicCollectionButton"),
            PortraitNodes.FindControl(submenu, "PotionLabButton"),
        };
        var stats = PortraitNodes.FindControl(submenu, "StatisticsButton");
        const float cardW = 368f;
        const float cardH = 490f;
        const float gap = 24f;
        var scale = Math.Min(
            1f,
            (canvas.X - 2f * PortraitHudMetrics.EdgeMargin - 2f * gap) / (3f * cardW)
        );
        var rowWidth = 3f * cardW * scale + 2f * gap;
        var rowX = PortraitHudMetrics.CenterX(canvas.X, rowWidth);
        var statsH = 200f * scale;
        var totalH = cardH * scale + 36f + statsH;
        var rowY = bandTop + (bandBottom - bandTop - totalH) * 0.5f;

        for (var i = 0; i < shelf.Length; i++)
        {
            var card = shelf[i];
            if (card is null)
                continue;
            if (card.GetParent() != submenu)
            {
                card.GetParent().RemoveChild(card);
                submenu.AddChild(card);
            }
            card.PivotOffset = Vector2.Zero;
            card.Scale = Vector2.One * scale;
            card.GlobalPosition = new Vector2(rowX + i * (cardW * scale + gap), rowY);
        }
        if (stats is not null)
        {
            if (stats.GetParent() != submenu)
            {
                stats.GetParent().RemoveChild(stats);
                submenu.AddChild(stats);
            }
            stats.PivotOffset = Vector2.Zero;
            stats.Scale = Vector2.One * scale;
            stats.GlobalPosition = new Vector2(
                PortraitHudMetrics.CenterX(canvas.X, 280f * scale),
                rowY + cardH * scale + 36f
            );
        }
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCompendiumSubmenu), "_Ready")]
internal static class CompendiumSubmenuPatch
{
    private static void Postfix(object __instance)
    {
        var submenu = (Control)__instance;
        PortraitCompendium.EnsureLoop(submenu);
    }
}

internal static class PortraitAncientEvent
{
    private const string SpacerName = "Sts2PortraitAncientSpacer";
    private const string LoopMeta = "Sts2PortraitAncientLoop";
    private const string OptionFitMeta = "Sts2PortraitAncientOptionFit";
    private const string BgAuthoredMeta = "Sts2PortraitAncientBgAuthored";
    private const float BgScale = 2.2f;
    private const float BubbleTopGap = 430f;
    private static readonly Vector2 BgAnchor = new(590f, 120f);
    private static readonly Vector2 BgShift = new(-280f, 0f);

    // The ancient's scene is a landscape Spine composition drawn at 1.12 in
    // a full-rect container: on the phone it filled the top third and left
    // the middle of the screen black down to the options. Scale it about a
    // top-center anchor so the speaker keeps its place and the scene grows
    // down to the options; the sides crop, which the wide painting affords.
    private static void FillBackground(Control layout, Vector2 canvas)
    {
        if (PortraitNodes.FindControl(layout, "AncientBgContainer") is not { } bg)
            return;
        if (!bg.HasMeta(BgAuthoredMeta))
            bg.SetMeta(BgAuthoredMeta, new Vector3(bg.Position.X, bg.Position.Y, bg.Scale.X));
        var authored = bg.GetMeta(BgAuthoredMeta).AsVector3();
        var authoredScale = Math.Max(authored.Z, 0.01f);
        var local = (BgAnchor - new Vector2(authored.X, authored.Y)) / authoredScale;
        // The speaker stands right of center in the wide painting; after the
        // zoom its face hung on the right edge, so the scene slides left.
        var target = BgAnchor - local * BgScale + BgShift;
        bg.PivotOffset = Vector2.Zero;
        if (Math.Abs(bg.Scale.X - BgScale) > 0.01f)
            bg.Scale = Vector2.One * BgScale;
        if (bg.Position.DistanceTo(target) > 1.5f)
            bg.Position = target;
    }
    private const float OptionTextHeight = 150f;
    private const float OptionRowHeight = 180f;

    // Each option's Text is a fixed 830x74 rich label (title line plus one
    // description line) with no wrapping, authored for a wide landscape row;
    // on the 1000-wide portrait row the description ran off the right edge
    // and was cut mid-sentence. Wrap it and give it a third line; the
    // 160-tall plate already has the room.
    internal static void FitOptionText(Control options)
    {
        foreach (var child in options.GetChildren())
        {
            if (child is not Control option || option.HasMeta(OptionFitMeta))
                continue;
            if (option.FindChild("Text", recursive: true, owned: false) is not RichTextLabel text)
                continue;
            // Only rows with a description need the room; a title-only row
            // ("Proceed") wrapped into a 150-tall rect came out at half size
            // from the label's own fitter, so it keeps its authored layout.
            var lines = text.Text.Split('\n');
            var hasDescription = lines.Length > 1 && lines[1].Trim().Length > 0;
            option.SetMeta(OptionFitMeta, true);
            if (!hasDescription)
                continue;
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.CustomMinimumSize = new Vector2(text.Size.X, OptionTextHeight);
            text.Size = new Vector2(text.Size.X, OptionTextHeight);
            // Three text lines run 141 tall; the authored 160 plate put the
            // next row's title on the last line. The plate grows with them.
            option.CustomMinimumSize = new Vector2(option.CustomMinimumSize.X, OptionRowHeight);
        }
    }

    // The layout's own intro tween keeps writing the authored (bottom
    // anchored) content position for a while after _Ready, so a one-shot
    // apply always loses; the same steady chain that guards the hand keeps
    // re-asserting until the screen leaves the tree.
    internal static void EnsureLoop(Control layout)
    {
        if (layout is null || !GodotObject.IsInstanceValid(layout) || layout.HasMeta(LoopMeta))
            return;
        layout.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(layout, () => Apply(layout));
    }

    // Ancient events (Neow and act ancients) stack the speech bubble and the
    // options in one bottom-anchored vbox, which buries the bubble at the
    // bottom of a portrait canvas while the speaker fills the top half. The
    // portrait composition stretches that vbox over the whole free band and
    // pushes a stretchy spacer between bubble and options: the bubble sits
    // up with the speaker, the options stay in the thumb zone, and the vbox
    // keeps doing its own sorting (BUG-014 rule: never fight a container).
    internal static void Apply(Control layout)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas)
            || layout is null
            || !GodotObject.IsInstanceValid(layout)
            || !layout.IsInsideTree())
            return;

        var container = PortraitNodes.FindControl(layout, "ContentContainer");
        var content = PortraitNodes.FindControl(layout, "Content");
        var dialogue = PortraitNodes.FindControl(layout, "DialogueContainer");
        var options = PortraitNodes.FindControl(layout, "OptionsContainer");
        if (container is null || content is null || dialogue is null || options is null)
            return;
        if (dialogue.GetParent() != content || options.GetParent() != content)
            return;

        var safeTop = PortraitDisplay.SafeTop();
        // The scene is zoomed 2.2x about a top anchor (FillBackground), which
        // puts the speaker's face in the band right under the bar; the bubble
        // hangs below that band so the face stays in view above the words.
        var top = Math.Max(
            PortraitHudMetrics.ContentTop(safeTop) + 26f,
            PortraitHudMetrics.HudBottom(safeTop) + BubbleTopGap
        );
        var bottom = canvas.Y - PortraitDisplay.SafeBottom() - 40f;

        PortraitNodes.ClearAnchors(container);
        container.Position = new Vector2(10f, top);
        container.Size = new Vector2(canvas.X - 20f, bottom - top);

        PortraitNodes.ClearAnchors(content);
        content.Position = new Vector2(80f, 0f);
        content.Size = new Vector2(canvas.X - 180f, bottom - top);

        // Touch rule: the bubble reads from an arm's length; scaling the
        // vbox child only grows its render into the empty spacer below.
        dialogue.PivotOffset = Vector2.Zero;
        dialogue.Scale = Vector2.One * 1.2f;

        Control spacer = null;
        foreach (var child in content.GetChildren())
        {
            if (child is Control c && c.Name == SpacerName)
            {
                spacer = c;
                break;
            }
        }
        if (spacer is null)
        {
            spacer = new Control
            {
                Name = SpacerName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            content.AddChild(spacer);
        }
        spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var optionsIndex = options.GetIndex();
        if (spacer.GetIndex() > optionsIndex)
            content.MoveChild(spacer, optionsIndex);
        else if (spacer.GetIndex() < optionsIndex - 1)
            content.MoveChild(spacer, optionsIndex - 1);

        FitOptionText(options);
        FillBackground(layout, canvas);

        // The "next" hint follows the bubble instead of floating at the
        // bottom edge of the screen.
        if (PortraitNodes.FindControl(layout, "FakeNextButtonContainer") is { } fakeNext)
            fakeNext.GlobalPosition = new Vector2(
                fakeNext.GlobalPosition.X,
                top + dialogue.Size.Y + 12f
            );
    }
}

// The card pick overlays ("Choose a Card" and the reward trio) are authored
// for a landscape center: on the phone the whole block filled a thin middle
// band with the top 40% and bottom 35% of the screen dead, and the Skip plate
// sat 20dp tall right under the cards. Grow the row to the side margins,
// hang the block from the content top, and give Skip a thumb plate with real
// separation from the cards.
internal static class PortraitCardPick
{
    private const string LoopMeta = "Sts2PortraitCardPickLoop";
    private const string SlotMeta = "Sts2PortraitCardPickSlot";

    internal static void EnsureLoop(Control screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || screen.HasMeta(LoopMeta))
            return;
        screen.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(screen, () => Apply(screen));
    }

    private static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var row = PortraitNodes.FindControl(screen, "CardRow");
        if (row is null)
            return;

        // Work on VISIBLE boxes, not authored sizes: the row's origin is not
        // its top-left and its Size is a landscape number, and trusting them
        // scattered cards half off-screen on the first try. GetGlobalRect
        // reflects the live transform, so scaling converges and placement is
        // origin-agnostic.
        var safeTop = PortraitDisplay.SafeTop();
        // The pick opens over the rewards overlay, where the bar is compact
        // (BUG-050): the grid hangs from the compact content top in every
        // case, so it does not drop 100 units after a fight.
        var y = PortraitHudMetrics.ContentTop(safeTop) + 16f;

        if (PortraitNodes.FindControl(screen, "Banner") is { Visible: true } banner)
        {
            // The banner ignores every reposition (its own tween owns it) and
            // parks behind the potion capsule. It carries no information the
            // grid does not, so portrait drops it and gives its band to cards.
            banner.Visible = false;
        }
        y += 28f;

        // Three cards side by side max out at ~350 units each on a 1180
        // canvas: unreadable, with a truck-sized hole under them. Mobile
        // wants BIG cards, so the holders leave the authored single row and
        // form a 2+1 grid (pairs per row, last row centered) at 1.5x; the
        // descriptions grow with them. Holders are positioned directly and
        // center-pivot scaled; CardRow itself is a zero-sized center anchor
        // the game owns, and it stays untouched.
        // Slots are pinned to each holder on first sight: the game moves the
        // hovered card to the front of the child list for z-order, and a grid
        // keyed on child order swapped the cards under the finger on the
        // press that should have selected one.
        var holders = new System.Collections.Generic.List<Control>();
        foreach (var child in row.GetChildren())
            if (child is Control { Visible: true } holder)
                holders.Add(holder);
        if (holders.Count == 0)
            return;
        var nextSlot = 0;
        foreach (var h in holders)
            if (h.HasMeta(SlotMeta))
                nextSlot = Math.Max(nextSlot, (int)h.GetMeta(SlotMeta) + 1);
        foreach (var h in holders)
            if (!h.HasMeta(SlotMeta))
                h.SetMeta(SlotMeta, nextSlot++);
        holders.Sort((a, b) => ((int)a.GetMeta(SlotMeta)).CompareTo((int)b.GetMeta(SlotMeta)));
        const float maxCardScale = 1.5f;
        const float gapX = 44f;
        const float gapY = 36f;
        const int perRow = 2;
        // The Skip plate and its gaps live under the grid; on a 16:9 canvas
        // the fixed 1.5x grid pushed it off the screen, so the scale is the
        // largest that keeps grid plus plate inside the content band.
        const float skipReserve = 200f;
        var rowCount = (holders.Count + perRow - 1) / perRow;
        var authoredW = holders[0].Size.X > 1f ? holders[0].Size.X : 350f;
        var authoredH = holders[0].Size.Y > 1f ? holders[0].Size.Y : 520f;
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());
        var gridRoom = bandBottom - skipReserve - y - gapY * (rowCount - 1);
        var fitScale = gridRoom / (rowCount * authoredH);
        var widthFit = (canvas.X - 2f * PortraitHudMetrics.EdgeMargin - gapX) / (perRow * authoredW);
        var cardScale = Mathf.Clamp(Math.Min(Math.Min(maxCardScale, fitScale), widthFit), 0.8f, maxCardScale);
        var cardW = authoredW * cardScale;
        var cardH = authoredH * cardScale;
        for (var i = 0; i < holders.Count; i++)
        {
            var r = i / perRow;
            var inRow = Math.Min(perRow, holders.Count - r * perRow);
            var cIdx = i % perRow;
            var rowWidth = inRow * cardW + (inRow - 1) * gapX;
            var x0 = (canvas.X - rowWidth) * 0.5f;
            var center = new Vector2(
                x0 + cardW * (cIdx + 0.5f) + gapX * cIdx,
                y + r * (cardH + gapY) + cardH * 0.5f
            );
            var h = holders[i];
            h.PivotOffset = h.Size * 0.5f;
            if (Math.Abs(h.Scale.X - cardScale) > 0.01f)
                h.Scale = Vector2.One * cardScale;
            // With a centered pivot the visual center sits at position plus
            // half the UNSCALED size; press tweens move scale, not position,
            // so only real drift is corrected.
            var target = center - h.Size * 0.5f;
            if (h.GlobalPosition.DistanceTo(target) > 2f)
                h.GlobalPosition = target;
        }
        y += rowCount * (cardH + gapY) + 40f;

        if (PortraitNodes.FindControl(screen, "RewardAlternatives") is { Visible: true } alts)
        {
            alts.PivotOffset = Vector2.Zero;
            alts.Scale = Vector2.One * 1.6f;
            var ar = alts.GetGlobalRect();
            var floorY = bandBottom - ar.Size.Y - 8f;
            var target = new Vector2(PortraitHudMetrics.CenterX(canvas.X, ar.Size.X), Math.Min(y, floorY));
            // The open tween animates this container in; only correct real
            // drift so presses are not cancelled mid-animation.
            if ((target - ar.Position).Length() > 3f)
                alts.GlobalPosition += target - ar.Position;
        }
    }
}

// The map's drag range is two landscape constants: the container's Y is
// nudged back into [-600, 1800] every frame, and the current row parks at
// -600 + row * distY. On a 1080-tall view that puts the bottom row near the
// bottom edge; on a 2596-tall portrait canvas the same -600 leaves the bottom
// 45 percent of the screen as bare parchment (the gap under the map). The
// lower bound moves down by a portrait allowance so the bottom row sits above
// the legend; every park below it glides there through the game's own lerp.
[HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
internal static class MapScrollRangePatch
{
    // 780 was measured on the 2596-tall canvas (bottom row just above the
    // legend); the allowance is the height beyond a 1816-tall canvas so a
    // 16:9 phone (2098) gets 282 and the row still lands on screen.
    private const float ReferenceHeight = 1816f;

    internal static float LowerBound()
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return -600f;
        return -600f + Math.Max(0f, canvas.Y - ReferenceHeight);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var portrait = OperatingSystem.IsAndroid();
        var lowerBound = AccessTools.Method(typeof(MapScrollRangePatch), nameof(LowerBound));
        foreach (var instruction in instructions)
        {
            if (portrait
                && instruction.opcode == System.Reflection.Emit.OpCodes.Ldc_R4
                && instruction.operand is float f
                && Math.Abs(f + 600f) < 0.01f)
            {
                yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, lowerBound);
                continue;
            }
            yield return instruction;
        }
    }
}

// Crystal Sphere (the divination mini-game some events open) is an overlay
// authored for landscape: sphere left, buttons and instructions right, the
// "divinations remain" line bottom-left. On the phone the right column and
// the footer left the screen. First pass: dump the tree once per open so the
// portrait composition can be measured, then place (see PortraitCrystalSphere).
[HarmonyPatch(
    typeof(MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere.NCrystalSphereScreen),
    "AfterOverlayOpened"
)]
internal static class CrystalSphereScreenPatch
{
    private static void Postfix(object __instance)
    {
        if (__instance is not Control screen)
            return;
        if (!screen.HasMeta("Sts2PortraitSphereDumped"))
        {
            screen.SetMeta("Sts2PortraitSphereDumped", true);
            PortraitNodes.DumpSubtree(screen, "sphere", 4);
        }
        PortraitCrystalSphere.EnsureLoop(screen);
    }
}

internal static class PortraitCrystalSphere
{
    private const string LoopMeta = "Sts2PortraitSphereLoop";

    internal static void EnsureLoop(Control screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || screen.HasMeta(LoopMeta))
            return;
        screen.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(screen, () => Apply(screen));
    }

    private const float RightUiMargin = 90f;
    private const float ProceedScale = 1.3f;

    // Measured tree: Bg (2560x1200 art at 1.04) carries the 792 Sphere at its
    // left third; Ui (1544 wide, starting at x -182) holds the footer line at
    // the far left and RightUi (buttons, spacer, a 976-tall instruction
    // block) at x 1101; the dialogue bubble sits at x 1291. Portrait: the
    // sphere centered right under the bar, the button/instruction column
    // under the sphere at 0.85, the footer line bottom-left, Proceed
    // bottom-right, the bubble in the sphere's empty top-right corner.
    private static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var safeTop = PortraitDisplay.SafeTop();
        var contentTop = PortraitHudMetrics.ContentTop(safeTop) + 20f;
        var contentBottom = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom());

        var bg = PortraitNodes.FindControl(screen, "Bg");
        var sphere = PortraitNodes.FindControl(screen, "Sphere");
        var sphereBottom = contentTop + 824f;
        if (bg is not null && sphere is not null)
        {
            var rect = sphere.GetGlobalRect();
            var center = rect.Position + rect.Size * 0.5f;
            var want = new Vector2(canvas.X * 0.5f, contentTop + rect.Size.Y * 0.5f);
            if (center.DistanceTo(want) > 1.5f)
                bg.Position += want - center;
            sphereBottom = contentTop + rect.Size.Y;
        }

        if (PortraitNodes.FindControl(screen, "RightUi") is { } right)
        {
            // The column is a 395-wide VBox whose children fill its width;
            // at that width the instruction text ran to ten lines and the
            // description label cut it. Give the column the portrait panel
            // width and the text wraps into a few lines under the buttons.
            PortraitNodes.ClearAnchors(right);
            right.PivotOffset = Vector2.Zero;
            if (Math.Abs(right.Scale.X - 1f) > 0.01f)
                right.Scale = Vector2.One;
            var width = canvas.X - 2f * RightUiMargin;
            if (Math.Abs(right.CustomMinimumSize.X - width) > 0.5f)
                right.CustomMinimumSize = new Vector2(width, 0f);
            if (Math.Abs(right.Size.X - width) > 0.5f)
                right.Size = new Vector2(width, right.Size.Y);
            var target = new Vector2(RightUiMargin, sphereBottom + 24f);
            if (right.GlobalPosition.DistanceTo(target) > 1.5f)
                right.GlobalPosition = target;
            // The instruction plate's inner VBox is placed by hand (28 in,
            // 339 wide) and never follows the plate; widen it to the plate.
            if (PortraitNodes.FindControl(right, "Instructions")?.GetNodeOrNull<Control>("VBoxContainer") is { } inner)
            {
                var innerWidth = width - 56f;
                if (Math.Abs(inner.Size.X - innerWidth) > 0.5f)
                {
                    inner.CustomMinimumSize = new Vector2(innerWidth, 0f);
                    inner.Size = new Vector2(innerWidth, inner.Size.Y);
                }
            }
        }

        if (PortraitNodes.FindControl(screen, "DivinationsLeft") is { } left)
        {
            PortraitNodes.ClearAnchors(left);
            var target = new Vector2(PortraitHudMetrics.EdgeMargin, contentBottom - 56f);
            if (left.GlobalPosition.DistanceTo(target) > 1.5f)
                left.GlobalPosition = target;
        }

        if (PortraitNodes.FindControl(screen, "ProceedButton") is { } proceed)
        {
            PortraitNodes.ClearAnchors(proceed);
            proceed.PivotOffset = Vector2.Zero;
            if (Math.Abs(proceed.Scale.X - ProceedScale) > 0.01f)
                proceed.Scale = Vector2.One * ProceedScale;
            var w = (proceed.Size.X > 1f ? proceed.Size.X : 269f) * ProceedScale;
            var h = (proceed.Size.Y > 1f ? proceed.Size.Y : 108f) * ProceedScale;
            var target = new Vector2(canvas.X - w - PortraitHudMetrics.EdgeMargin, contentBottom - h - 8f);
            if (proceed.GlobalPosition.DistanceTo(target) > 1.5f)
                proceed.GlobalPosition = target;
        }

        if (screen.FindChild("Dialogue", recursive: false, owned: false) is Node2D dialogue)
        {
            var target = new Vector2(canvas.X - 300f, contentTop + 90f);
            if (dialogue.Position.DistanceTo(target) > 1.5f)
                dialogue.Position = target;
        }
    }
}

// The grid select overlays (upgrade, transform, enchant, deck picks) inherit
// NCardGridSelectionScreen, not NCardsViewScreen, so the deck view's tickbox
// growth never reached their "View Upgrades" box. AfterOverlayOpened has an
// empty body here: the safe hook.
[HarmonyPatch(
    typeof(MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardGridSelectionScreen),
    "AfterOverlayOpened"
)]
internal static class GridSelectTickboxPatch
{
    private static void Postfix(object __instance)
    {
        var screen = (Control)__instance;
        PortraitNodes.AssertLoop(screen, () =>
        {
            if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
                return;
            // The grid's first row sat under the top bar (the authored
            // YOffset assumes a landscape header). Push the offset down to
            // the content top once and let the game's own reflow re-lay the
            // rows; the grid scrolls, so nothing is lost at the bottom.
            if (!screen.HasMeta("Sts2PortraitGridOffset")
                && PortraitNodes.FindControl(screen, "CardGrid") is { } grid
                && grid.Size.Y > 1f)
            {
                screen.SetMeta("Sts2PortraitGridOffset", true);
                try
                {
                    var contentTop = PortraitHudMetrics.ContentTop(PortraitDisplay.SafeTop());
                    var firstRowTop = grid.GlobalPosition.Y + 80f;
                    var current = (int)(Traverse.Create(grid).Property("YOffset").GetValue() ?? 0);
                    var shift = (int)Math.Max(0f, contentTop - firstRowTop);
                    Traverse.Create(grid).Property("YOffset").SetValue(current + shift);
                    AccessTools.Method(grid.GetType(), "ReflowColumns")?.Invoke(grid, null);
                    PatchHelper.Log($"[Portrait] grid offset +{shift} (top {firstRowTop:F0} -> {contentTop:F0})");
                }
                catch (Exception ex)
                {
                    PatchHelper.Log($"[Portrait] grid offset failed: {ex.GetBaseException().Message}");
                }
            }
            PortraitGridStrip.Place(screen);
        });
    }
}

[HarmonyPatch(
    typeof(MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen),
    "AfterOverlayOpened"
)]
internal static class CardRewardScreenPatch
{
    private static void Postfix(object __instance)
    {
        // AfterOverlayOpened tweens the Skip plate's position for 0.5 s with
        // a Back ease toward its authored spot; the portrait loop re-places
        // it every frame, and the two writers made the plate bounce up and
        // down until the tween ended. Drop the tween; the loop places it.
        try
        {
            if (Traverse.Create(__instance).Field("_buttonTween").GetValue() is Tween tween && tween.IsValid())
                tween.Kill();
        }
        catch
        {
            // Field gone after a game update: the bounce returns, nothing breaks.
        }
        PortraitCardPick.EnsureLoop((Control)__instance);
    }
}

[HarmonyPatch(
    typeof(MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NChooseACardSelectionScreen),
    "AfterOverlayOpened"
)]
internal static class ChooseACardScreenPatch
{
    private static void Postfix(object __instance)
        => PortraitCardPick.EnsureLoop((Control)__instance);
}

internal static class PortraitRewards
{
    private const string AuthoredMeta = "Sts2PortraitRewardsAuthored";
    private const string LoopMeta = "Sts2PortraitRewardsLoop";

    // Set by the map pass when it hides a live loot screen under the map;
    // the hand guard restores the screen and clears the mark on map close.
    internal const string MapHidMeta = "Sts2PortraitRewardsMapHid";

    internal static void EnsureLoop(Control screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || screen.HasMeta(LoopMeta))
            return;
        screen.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(screen, () => Apply(screen));
    }

    // Entry point for the hand guard, which re-drives this pass because the
    // screen's own loop has died silently on device more than once.
    internal static void ApplyNow(Control screen) => Apply(screen);

    // The loot panel is authored for a landscape center: a 526x640 plate in
    // the middle of a 2596-tall canvas reads as a postage stamp with dead
    // space above and below. Touch rules: the panel grows through FillScale
    // until it presses against the side margins, hangs from the content top,
    // and the proceed control grows into a thumb-sized plate hanging from
    // the bottom of the band.
    private static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        // A capstone screen (deck view opened from the rewards) draws over
        // everything at its own layer; the Z-pinned proceed arrow punched
        // through it. Hand the arrow back to normal layering while one is
        // open and skip the layout pass.
        PortraitNodes.DumpRegionOnRequest(screen);
        if (PortraitCapstone.IsOpen(screen) || !RewardsIsTopOverlay(screen))
        {
            // Silent success cost a full diagnosis once: one-shot breadcrumbs
            // per branch so a trace always shows which path this pass took.
            if (!screen.HasMeta("Sts2PortraitRewardsIdleLogged"))
            {
                screen.SetMeta("Sts2PortraitRewardsIdleLogged", true);
                PatchHelper.Log("[Portrait] rewards pass idle: covered or capstone open");
            }
            if (PortraitNodes.FindControl(screen, "ProceedButton") is { } pinned)
            {
                // Relative alone is not enough: a relative 460 still wins
                // inside the same canvas layer, and the card selection
                // overlay lives in the same layer as the rewards screen.
                pinned.ZAsRelative = true;
                pinned.ZIndex = 0;
            }
            return;
        }
        if (!screen.HasMeta("Sts2PortraitRewardsActiveLogged"))
        {
            screen.SetMeta("Sts2PortraitRewardsActiveLogged", true);
            PatchHelper.Log("[Portrait] rewards layout active");
        }
        var safeTop = PortraitDisplay.SafeTop();
        var safeBottom = PortraitDisplay.SafeBottom();
        // Rewards show over the finished combat room, and the bar goes
        // compact for them (BUG-050); the band starts below the compact bar
        // whether the room underneath is combat or not, so the loot after a
        // fight sits where the loot after a resume does.
        var bandTop = PortraitHudMetrics.ContentTop(safeTop);
        var bandBottom = PortraitHudMetrics.ContentBottom(canvas.Y, safeBottom);
        var panelBottom = bandBottom;

        if (PortraitNodes.FindControl(screen, "Rewards") is { } panel)
        {
            // The authored panel (526x640) masks a 484-tall list: four and a
            // half rows. A five-reward loot (the crystal sphere's) hid its
            // last row behind the mask edge. The panel and the mask grow by
            // the missing rows before the panel is scaled to the band.
            if (!panel.HasMeta(AuthoredMeta))
                panel.SetMeta(AuthoredMeta, panel.Size);
            var authored = panel.GetMeta(AuthoredMeta).AsVector2();
            var mask = PortraitNodes.FindControl(panel, "RewardContainerMask");
            var list = PortraitNodes.FindControl(panel, "RewardsContainer");
            var extra = 0f;
            if (mask is not null && list is not null)
            {
                if (!mask.HasMeta(AuthoredMeta))
                    mask.SetMeta(AuthoredMeta, mask.Size);
                var maskAuthored = mask.GetMeta(AuthoredMeta).AsVector2();
                var needed = list.Size.Y + 70f;
                extra = Math.Max(0f, needed - maskAuthored.Y);
                var maskSize = new Vector2(maskAuthored.X, maskAuthored.Y + extra);
                if (mask.Size.DistanceTo(maskSize) > 0.5f)
                    mask.Size = maskSize;
            }
            var panelSize = new Vector2(authored.X, authored.Y + extra);
            if (panel.Size.DistanceTo(panelSize) > 0.5f)
                panel.Size = panelSize;
            var baseW = panelSize.X > 1f ? panelSize.X : 526f;
            var baseH = panelSize.Y > 1f ? panelSize.Y : 640f;
            // The proceed strip keeps the lower part of the band.
            var scale = PortraitHudMetrics.FillScale(
                baseW,
                baseH,
                canvas.X - PortraitHudMetrics.EdgeMargin * 2f,
                bandBottom - bandTop - 260f,
                2.2f
            );
            panel.PivotOffset = Vector2.Zero;
            panel.Scale = Vector2.One * scale;
            // Center the panel-plus-proceed group in the band: hanging from
            // the band top read fine on short buckets but left a hole under
            // the Skip arrow once the panel hit its scale cap on this screen.
            var panelH = baseH * scale;
            const float proceedBand = 210f;
            var slack = bandBottom - bandTop - panelH - proceedBand;
            var panelTop = bandTop + Math.Max(24f, slack * 0.5f);
            panel.GlobalPosition = new Vector2(
                PortraitHudMetrics.CenterX(canvas.X, baseW * scale),
                panelTop
            );
            panelBottom = panelTop + panelH;
        }

        if (PortraitNodes.FindControl(screen, "ProceedButton") is { Visible: true } proceed)
        {
            // No scale from this layer: the press animation writes the
            // control's own Scale, and the two writers fighting shrank the
            // arrow under the finger and cancelled the press. The game DOES
            // scale it though (1.5x on this screen), so the placement math
            // uses the visual size or the arrowhead pokes off the canvas.
            var proceedScale = Math.Max(proceed.GetGlobalTransform().Scale.X, 0.01f);
            var baseW = (proceed.Size.X > 1f ? proceed.Size.X : 269f) * proceedScale;
            var baseH = (proceed.Size.Y > 1f ? proceed.Size.Y : 108f) * proceedScale;
            // Anchors under the reward rows themselves (the mask is the real
            // content edge); panel-height guesses drifted into the hand fan
            // on short buckets.
            var anchorBottom = panelBottom;
            if (PortraitNodes.FindControl(screen, "RewardContainerMask") is { } mask)
                anchorBottom = mask.GlobalPosition.Y + mask.Size.Y * mask.GetGlobalTransform().Scale.Y;
            proceed.ZAsRelative = false;
            proceed.ZIndex = 460;
            // The arrow art overhangs its control rect by ~40 units on the
            // right, like every torn-banner button in this game; the margin
            // covers the art, not just the rect.
            var target = new Vector2(
                canvas.X - PortraitHudMetrics.EdgeMargin - baseW - 64f,
                Math.Min(anchorBottom + 36f, bandBottom - baseH)
            );
            // Repositioning every assert tick cancelled presses mid-animation;
            // only correct real drift.
            if (proceed.GlobalPosition.DistanceTo(target) > 3f)
                proceed.GlobalPosition = target;
        }
    }
    // "Choose a Card" and its cousins are separate overlays pushed above the
    // rewards screen on the game's own overlay stack, and the Z-pinned proceed
    // arrow punched through them: two Skips at once. The stack itself is the
    // source of truth for whether something covers this screen.
    private static bool RewardsIsTopOverlay(Control screen)
    {
        try
        {
            var stackType = AccessTools.TypeByName(
                "MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack"
            );
            var instance = stackType
                ?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (instance is null)
                return true;

            var peek = stackType
                .GetMethod("Peek", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(instance, null);
            return peek is null || ReferenceEquals(peek, screen);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Portrait] overlay stack peek failed: {ex.GetBaseException().Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen), "_Ready")]
internal static class RewardsScreenPatch
{
    private static void Postfix(object __instance)
    {
        var screen = (Control)__instance;
        PortraitRewards.EnsureLoop(screen);
    }
}

internal static class PortraitTreasure
{
    private const string LoopMeta = "Sts2PortraitTreasureLoop";

    internal static void EnsureLoop(Node room)
    {
        if (room is null || !GodotObject.IsInstanceValid(room) || room.HasMeta(LoopMeta))
            return;
        room.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(room, () => Apply(room));
    }

    // The chest is a 800x500 button floating in a dark landscape room; in
    // portrait that leaves the top and bottom thirds pitch black. Touch
    // rules: grow it through FillScale until it presses the side margins
    // and center it in the free band; the post-open relic reveal gets the
    // same treatment.
    private static void Apply(Node room)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var safeTop = PortraitDisplay.SafeTop();
        var safeBottom = PortraitDisplay.SafeBottom();
        var bandTop = PortraitHudMetrics.ContentTop(safeTop);
        var bandHeight = PortraitHudMetrics.ContentBandHeight(canvas.Y, safeTop, safeBottom);

        PlaceCentered(PortraitNodes.FindControl(room, "Chest"), 800f, 500f);
        if (PortraitNodes.FindControl(room, "RelicCollection") is { Visible: true } relics)
        {
            PlaceCentered(relics, 900f, 580f);
            // The collection is a landscape-sized box, so fitting it to the
            // band barely scales it and the single relic inside stayed a
            // ~22dp icon in the middle of a dark screen. The holder itself
            // grows about its center into a thumb-sized pick.
            if (PortraitNodes.FindControl(relics, "SingleplayerRelicHolder") is { Visible: true } holder
                && holder.Size.X > 1f)
            {
                const float holderScale = 2.6f;
                holder.PivotOffset = holder.Size * 0.5f;
                if (Math.Abs(holder.Scale.X - holderScale) > 0.01f)
                    holder.Scale = Vector2.One * holderScale;
            }
        }

        void PlaceCentered(Control control, float fallbackW, float fallbackH)
        {
            if (control is null)
                return;
            var baseW = control.Size.X > 1f ? control.Size.X : fallbackW;
            var baseH = control.Size.Y > 1f ? control.Size.Y : fallbackH;
            var scale = PortraitHudMetrics.FillScale(
                baseW,
                baseH,
                canvas.X - PortraitHudMetrics.EdgeMargin * 2f,
                bandHeight * 0.8f,
                1.75f
            );
            control.PivotOffset = Vector2.Zero;
            control.Scale = Vector2.One * scale;
            control.GlobalPosition = new Vector2(
                PortraitHudMetrics.CenterX(canvas.X, baseW * scale),
                bandTop + (bandHeight - baseH * scale) * 0.5f
            );
        }
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom), "_Ready")]
internal static class TreasureRoomPatch
{
    private static void Postfix(object __instance)
    {
        var room = (Node)__instance;
        PortraitTreasure.EnsureLoop(room);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout), "_Ready")]
internal static class AncientEventReadyPatch
{
    private static void Postfix(object __instance)
    {
        var layout = (Control)__instance;
        PortraitAncientEvent.EnsureLoop(layout);
    }
}

// SetDialogueLineAndAnimate is unpatchable: its body reads the protected base
// field _optionsContainer (BUG-031). The tap-to-advance handler is the safe
// hook with the same timing, and its body only touches this class's own
// members; the initial line 0 is covered by the _Ready assert loop anyway.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout), "OnDialogueHitboxClicked")]
internal static class AncientEventDialoguePatch
{
    private static void Postfix(object __instance) =>
        PortraitAncientEvent.Apply((Control)__instance);
}

[HarmonyPatch(typeof(NAncientNameBanner), "_Ready")]
internal static class NeowBannerPatch
{
    private static void Postfix(Node __instance)
    {
        if (PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize) && __instance is CanvasItem item)
            item.Visible = false;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.NAncientBgContainer), "OnWindowChange")]
internal static class NeowBackgroundPatch
{
    private static void Postfix(Control __instance)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;
        __instance.PivotOffset = __instance.Size * 0.5f;
        __instance.Scale = Vector2.One * 1.12f;
        __instance.Position = new Vector2(-140f, -30f);
    }
}

// Patching this button's _Ready used to do the sizing, but a patched method
// runs as a generated copy that cannot reach the protected members the
// original called, so ConnectSignals threw MethodAccessException, the button
// never finished setting up and the combat room found a null background. The
// position getter is a safe hook: it runs whenever the button is placed.
[HarmonyPatch(typeof(NProceedButton), "ShowPos", MethodType.Getter)]
internal static class ProceedButtonPatch
{
    private const float MaxWidthRatio = 0.36f;
    private const float MaxHeight = 170f;
    private const float MaxScale = 1.5f;
    private const float EdgeGap = 20f;

    private static void Postfix(object __instance, ref Vector2 __result)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var button = (Control)__instance;
        ApplyScale(button, canvas);

        var width = (button.Size.X > 1f ? button.Size.X : 300f) * Math.Max(button.Scale.X, 1f);
        // Same clamp as the rewards pass target: edge margin plus the ~40
        // units of banner art overhanging the control rect. Two different
        // clamps had the arrow ping-ponging between two X positions.
        __result.X = Math.Min(__result.X, canvas.X - width - PortraitHudMetrics.EdgeMargin - 64f);

        // The game's Y aims at the landscape composition and parked the
        // arrow ON the resting hero's chest. The arrow always drops to the
        // bottom content band, under any room's character art.
        var height = (button.Size.Y > 1f ? button.Size.Y : 108f) * Math.Max(button.Scale.Y, 1f);
        var floorY = PortraitHudMetrics.ContentBottom(canvas.Y, PortraitDisplay.SafeBottom())
            - height - 8f;
        __result.Y = Math.Max(__result.Y, floorY);
    }

    private static void ApplyScale(Control button, Vector2 canvas)
    {
        var baseW = button.Size.X > 1f ? button.Size.X : 269f;
        var baseH = button.Size.Y > 1f ? button.Size.Y : 108f;
        var scale = PortraitHudMetrics.FillScale(baseW, baseH, canvas.X * MaxWidthRatio, MaxHeight, MaxScale);
        if (Math.Abs(button.Scale.X - scale) <= 0.01f)
            return;

        button.PivotOffset = Vector2.Zero;
        button.Scale = Vector2.One * scale;
    }
}

internal static class PortraitGameOver
{
    private const string LoopMeta = "Sts2PortraitGameOverLoop";

    internal static void EnsureLoop(Control screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || screen.HasMeta(LoopMeta))
            return;
        screen.SetMeta(LoopMeta, true);
        PortraitNodes.AssertLoop(screen, () => Apply(screen));
    }

    // Entry point for the hand guard, which re-drives this pass because the
    // screen's own loop has died silently on device more than once.
    internal static void ApplyNow(Control screen) => Apply(screen);

    // Touch rules for the run-end buttons: lift them to the touch minimum
    // and hang them from the bottom of the band, stacked when both show.
    private static void Apply(Control screen)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var safeBottom = PortraitDisplay.SafeBottom();
        var nextBottom = PortraitHudMetrics.ContentBottom(canvas.Y, safeBottom);
        foreach (var name in new[] { "ContinueButton", "LeaderboardButton" })
        {
            if (PortraitNodes.FindControl(screen, name) is not { Visible: true } button)
                continue;
            var baseW = button.Size.X > 1f ? button.Size.X : 260f;
            var baseH = button.Size.Y > 1f ? button.Size.Y : 58f;
            var scale = PortraitHudMetrics.TouchScale(baseW, baseH, 1.8f);
            var height = baseH * scale;
            button.PivotOffset = Vector2.Zero;
            button.Scale = Vector2.One * scale;
            button.GlobalPosition = new Vector2(
                PortraitHudMetrics.CenterX(canvas.X, baseW * scale),
                nextBottom - height
            );
            nextBottom -= height + 28f;
        }
    }
}

// _Ready is unpatchable since the game update: its body builds a RunHistory
// through an object initializer whose Win setter is now init-only, and Mono
// cannot resolve the modreq'd accessor while rebuilding the copy ("Method not
// found: set_Win"). The static Create factory is the safe hook; the assert
// loop idles until the returned screen actually enters the tree.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen.NGameOverScreen), "Create")]
internal static class GameOverScreenPatch
{
    private static void Postfix(object __result)
    {
        if (__result is Control screen)
            PortraitGameOver.EnsureLoop(screen);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
internal static class CharacterSelectPatch
{
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        if (!PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            return;
        var panel = Traverse.Create(__instance).Field("_infoPanel").GetValue<Control>();
        if (panel is null)
            return;
        Traverse.Create(__instance).Field("_infoPanelTween").GetValue<Tween>()?.Kill();
        panel.Position = new Vector2(40f, panel.Position.Y);
        Traverse.Create(__instance).Field("_infoPanelPosFinalVal").SetValue(panel.Position);

        // Switching characters slides the confirm button toward its
        // landscape target, which can carry it past the canvas edge
        // (found via the Silent: the button parked at x 1300 on an 1180
        // canvas, untappable). Keep it clamped for the screen's lifetime.
        var confirm = PortraitNodes.FindControl(__instance, "ConfirmButton");
        if (confirm is not null && !confirm.HasMeta("Sts2PortraitConfirmClamp"))
        {
            confirm.SetMeta("Sts2PortraitConfirmClamp", true);
            PortraitNodes.AssertLoop(confirm, () =>
            {
                var canvas = PortraitDisplay.CanvasSize;
                var width = confirm.Size.X * Math.Max(confirm.Scale.X, 1f);
                var maxX = canvas.X - width - 20f;
                if (confirm.GlobalPosition.X > maxX)
                    confirm.GlobalPosition = new Vector2(maxX, confirm.GlobalPosition.Y);
            });
        }
    }
}
