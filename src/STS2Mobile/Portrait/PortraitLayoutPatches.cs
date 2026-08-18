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

    internal static void After(Node node, double delay, Action action)
    {
        node.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
                action();
        };
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

[HarmonyPatch(typeof(NGame), "OnWindowChange")]
internal static class GameWindowChangePatch
{
    private static bool Prefix() => !PortraitDisplay.Apply();
}

[HarmonyPatch(typeof(NGlobalUi), "OnWindowChange")]
internal static class GlobalUiWindowChangePatch
{
    private static bool Prefix() => !PortraitDisplay.Apply();
}

internal static class PortraitMainMenu
{
    private const float BackgroundHeight = 1200f;
    private const float BackgroundWidth = 2560f;
    private const float ButtonScale = 1.65f;

    internal static void Apply(NMainMenu menu)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;

        var center = canvas * 0.5f;
        var background = menu.GetNodeOrNull<Control>("MainMenuBg/BgContainer");
        var parentScale = 1f;
        if (background is not null)
        {
            parentScale = Mathf.Max(canvas.X / BackgroundWidth, canvas.Y / BackgroundHeight) * 1.04f;
            background.PivotOffset = new Vector2(BackgroundWidth, BackgroundHeight) * 0.5f;
            background.Scale = Vector2.One * parentScale;
            background.Position = center - new Vector2(BackgroundWidth, BackgroundHeight) * 0.5f;
        }

        var logo = menu.FindChild("Logo", recursive: true, owned: false) as Node2D;
        if (logo is not null)
        {
            logo.Scale = Vector2.One * (0.42f / parentScale);
            logo.GlobalPosition = new Vector2(center.X - 460f, canvas.Y * 0.18f);
            logo.Visible = true;
            logo.Modulate = new Color(logo.Modulate.R, logo.Modulate.G, logo.Modulate.B, 1f);
        }

        var buttons = menu.GetNodeOrNull<Control>("MainMenuTextButtons")
            ?? menu.GetNodeOrNull<Control>("%MainMenuTextButtons");
        if (buttons is not null)
        {
            PortraitNodes.ClearAnchors(buttons);
            var width = buttons.Size.X > 1f ? buttons.Size.X : 300f;
            var height = buttons.Size.Y > 1f ? buttons.Size.Y : 220f;
            buttons.PivotOffset = new Vector2(width, height) * 0.5f;
            buttons.Scale = Vector2.One * ButtonScale;
            buttons.Position = new Vector2(center.X - width * 0.5f, canvas.Y * 0.54f);
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
        => PortraitNodes.After(__instance, 0.35, () => PortraitMainMenu.Apply(__instance));
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
    internal static bool IsOpen(Node anchor)
    {
        if (anchor is null || !GodotObject.IsInstanceValid(anchor) || !anchor.IsInsideTree())
            return false;
        var capstone = PortraitNodes.FindByType(anchor.GetTree().Root, "NCapstoneContainer");
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
    private const float FanCompression = 1.00f;
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

    internal static float CompressFan(float value) => value * FanCompression;

    // Card art reaches roughly this far below the fan's baseline at hand
    // scale; the baseline must keep that plus the safe-area inset on screen.
    private const float HandBottomClearance = 240f;

    private const string HandGuardMeta = "sts2_portrait_hand_guard";
    private const string HandHiddenMeta = "sts2_portrait_hand_hidden_for_capstone";

    // Fullscreen capstone screens (deck view, in-run settings) sit at plain
    // tree z, and the fan's absolute ZIndex would draw the cards over them.
    // Hide the holder while such a screen is open; restore only what we hid.
    private static void ApplyCapstoneHandVisibility(Node hand, Control holder)
    {
        var open = PortraitCapstone.IsOpen(hand);

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
    }

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
        // local Position put the fan mid-screen there.
        holder.Position += new Vector2(0f, HandBaselineY(canvas) - holder.GlobalPosition.Y);
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
        if (!GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree())
            return;
        hand.GetTree().CreateTimer(0.5).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree())
                return;
            var canvas = PortraitDisplay.CanvasSize;
            if (PortraitDisplay.IsPortrait(canvas))
            {
                var holder = PortraitNodes.FindControl(hand, "CardHolderContainer");
                if (holder is not null)
                {
                    ApplyCapstoneHandVisibility(hand, holder);
                    if (Math.Abs(holder.GlobalPosition.Y - HandBaselineY(canvas)) > 4f)
                    {
                        var before = holder.GlobalPosition.Y;
                        PlaceHand(holder, canvas);
                        PatchHelper.Log(
                            $"[Portrait] Hand guard corrected holder Y {before:F0} -> {holder.GlobalPosition.Y:F0}"
                        );
                    }
                }
            }
            ScheduleHandGuard(hand);
        };
    }


    internal static void PlaceEndTurn(Control button, Vector2 canvas)
    {
        const float scale = 1.18f;
        PortraitNodes.ClearAnchors(button);
        button.PivotOffset = Vector2.Zero;
        button.Scale = Vector2.One * scale;
        var width = button.Size.X > 1f ? button.Size.X : 250f;
        var target = new Vector2(canvas.X - width * scale - 38f, canvas.Y * 0.73f);
        button.Position += target - button.GlobalPosition;
        button.ZAsRelative = false;
        button.ZIndex = 420;
    }

    internal static void PlacePile(Control pile, Vector2 canvas, bool onRight)
    {
        const float scale = 1.42f;
        const float margin = 24f;
        PortraitNodes.ClearAnchors(pile);
        pile.PivotOffset = Vector2.Zero;
        pile.Scale = Vector2.One * scale;
        var width = pile.Size.X > 1f ? pile.Size.X : 86f;
        var height = pile.Size.Y > 1f ? pile.Size.Y : 86f;
        var x = onRight ? canvas.X - width * scale - margin : margin;
        var y = canvas.Y - height * scale - PortraitDisplay.SafeBottom() - 12f;
        pile.Position += new Vector2(x, y) - pile.GlobalPosition;
        pile.ZAsRelative = false;
        pile.ZIndex = 520;
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
    private static void Postfix(ref Vector2 __result)
    {
        if (PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
            __result.X = PortraitCombat.CompressFan(__result.X);
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

    // The settings tab row is authored at y=102, inside the cutout zone on
    // devices with a deep inset. Shift the tab manager and the scroll body
    // down below the safe top while the screen is open.
    private static void OffsetContentBelowSafeTop(NSettingsScreen screen, bool open)
    {
        var tabs = PortraitNodes.FindControl(screen, "SettingsTabManager");
        var scroll = PortraitNodes.FindControl(screen, "ScrollContainer");
        if (tabs is null)
            return;

        if (open)
        {
            if (screen.HasMeta(ContentOffsetMeta))
                return;
            var wanted = PortraitDisplay.SafeTop() + 8f;
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

    private static void HideBuildWatermark(Node root, Vector2 canvas)
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
        // they always get the slim bar.
        var combat = PortraitCombat.CombatHudActive && !IsCapstoneScreenOpen(bar);
        var left = bar.GetNodeOrNull<Control>("LeftAlignedStuff");
        var right = bar.GetNodeOrNull<Control>("RightAlignedStuff");

        var hp = PortraitNodes.FindControl(bar, "TopBarHp");
        var gold = PortraitNodes.FindControl(bar, "TopBarGold");
        var portrait = PortraitNodes.FindControl(bar, "TopBarPortrait");
        var portraitTip = PortraitNodes.FindControl(bar, "TopBarPortraitTip");
        var potions = PortraitNodes.FindControl(bar, "PotionContainer");
        var room = PortraitNodes.FindControl(bar, "RoomIcon");
        var floor = PortraitNodes.FindControl(bar, "FloorIcon");
        var boss = PortraitNodes.FindControl(bar, "BossIcon");
        var map = PortraitNodes.FindControl(bar, "Map");
        var deck = PortraitNodes.FindControl(bar, "Deck");
        var pause = PortraitNodes.FindControl(bar, "PauseButton", "Pause");
        var timer = PortraitNodes.FindControl(bar, "TimerContainer");

        if (portrait is not null)
            portrait.Visible = false;
        if (portraitTip is not null)
            portraitTip.Visible = false;
        if (timer is not null)
            timer.Visible = false;

        var parent = bar.GetParent();
        var relics = parent is null ? null : PortraitNodes.FindControl(parent, "RelicInventory");
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
            // Combat pins these two as grandchildren of the row (Place writes
            // their transforms directly); their margin-container slots do not
            // re-sort them on the way back to the slim bar, so hand them back
            // explicitly or they linger at the combat coordinates (BUG-014).
            RestoreIntoSlot(potions);
            RestoreIntoSlot(room);
            if (right is not null)
            {
                const float rightScale = 1.05f;
                var width = right.Size.X > 1f ? right.Size.X : 340f;
                PlaceRow(right, new Vector2(canvas.X - 30f - width * rightScale, top), rightScale);
            }

            PlaceRelics(relics, canvas, new Vector2(38f, row2 + 10f), 1.12f, canvas.X - 68f);
        }

        var signature = $"portrait-zones-6:{canvas.X:F0}:{(combat ? "combat" : "compact")}:{relics?.GetChildCount() ?? 0}";
        if (_lastSignature != signature)
        {
            _lastSignature = signature;
            // Sweep only on transitions: a full-tree walk is too expensive for
            // the perpetual reflow tick, and watermark labels only (re)appear
            // alongside scene or mode changes.
            HideBuildWatermark(bar.GetTree().Root, canvas);
            PatchHelper.Log($"[Portrait] Top bar reflow {signature}");
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
        backdrop.Size = new Vector2(canvas.X, PortraitHudMetrics.HudBottom(safeTop) + 96f);
    }
}

[HarmonyPatch(typeof(NContinueRunInfo), "AnimShow")]
internal static class ContinueRunInfoPatch
{
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
        var target = new Vector2(
            (canvas.X - width * globalScale) * 0.5f,
            Mathf.Clamp(
                canvas.Y * 0.27f,
                PortraitDisplay.SafeTop() + 80f,
                canvas.Y - height * globalScale - 180f
            )
        );
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
        PortraitNodes.After(room, 0.25, () =>
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

            const float textScale = 1.1f;
            block.PivotOffset = Vector2.Zero;
            block.Scale = Vector2.One * textScale;
            var blockWidth = (block.Size.X > 1f ? block.Size.X : 800f) * textScale;
            block.GlobalPosition = new Vector2((canvas.X - blockWidth) * 0.5f, contentTop);

            var optionsTop = canvas.Y;
            if (options is not null)
            {
                const float optionsScale = 1.18f;
                options.PivotOffset = Vector2.Zero;
                options.Scale = Vector2.One * optionsScale;
                var optionsWidth = (options.Size.X > 1f ? options.Size.X : 800f) * optionsScale;
                var optionsHeight = (options.Size.Y > 1f ? options.Size.Y : 220f) * optionsScale;
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
        const float utilityBand = 190f;
        var cellWidth = panelWidth / columns;
        var cellHeight = (panelHeight - utilityBand) / rows;
        var scale = Mathf.Clamp(cellHeight / 500f, 0.72f, 1.05f);
        var origin = slots.GlobalPosition;

        for (var i = 0; i < cards.Count; i++)
            Place(cards[i], origin + CellCenter(i, cellWidth, cellHeight), scale);
        if (hasRemoval)
            Place(removal, origin + CellCenter(cards.Count, cellWidth, cellHeight), scale);

        // The utility band holds two shelves side by side; at 0.95 scale the
        // right shelf's last potion ran off the canvas and the rows touched.
        var relics = PortraitNodes.FindControl(slots, "Relics");
        var potions = PortraitNodes.FindControl(slots, "Potions");
        var bandY = panelHeight - utilityBand + 42f;
        if (relics is not null)
            Place(relics, origin + new Vector2(panelWidth * 0.22f, bandY), 0.8f);
        if (potions is not null)
            Place(potions, origin + new Vector2(panelWidth * 0.64f, bandY), 0.8f);
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
        PortraitNodes.After(room, 0.15, () =>
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
        PortraitNodes.After(room, 0.15, () =>
        {
            var background = PortraitNodes.FindControl(room, "BgContainer");
            if (background is null || !PortraitDisplay.IsPortrait(PortraitDisplay.CanvasSize))
                return;
            background.PivotOffset = background.Size * 0.5f;
            background.Scale = Vector2.One * 1.72f;
        });
    }
}

internal static class PortraitAncientEvent
{
    private const string SpacerName = "Sts2PortraitAncientSpacer";
    private const string LoopMeta = "Sts2PortraitAncientLoop";

    // The layout's own intro tween keeps writing the authored (bottom
    // anchored) content position for a while after _Ready, so a one-shot
    // apply always loses; the same steady chain that guards the hand keeps
    // re-asserting until the screen leaves the tree.
    internal static void EnsureLoop(Control layout)
    {
        if (layout is null || !GodotObject.IsInstanceValid(layout) || layout.HasMeta(LoopMeta))
            return;
        layout.SetMeta(LoopMeta, true);
        Tick(layout);
    }

    private static void Tick(Control layout)
    {
        if (!GodotObject.IsInstanceValid(layout) || !layout.IsInsideTree())
            return;
        Apply(layout);
        PortraitNodes.After(layout, 0.5, () => Tick(layout));
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
        var top = PortraitHudMetrics.ContentTop(safeTop) + 26f;
        var bottom = canvas.Y - PortraitDisplay.SafeBottom() - 24f;

        PortraitNodes.ClearAnchors(container);
        container.Position = new Vector2(10f, top);
        container.Size = new Vector2(canvas.X - 20f, bottom - top);

        PortraitNodes.ClearAnchors(content);
        content.Position = new Vector2(80f, 0f);
        content.Size = new Vector2(canvas.X - 180f, bottom - top);

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

        // The "next" hint follows the bubble instead of floating at the
        // bottom edge of the screen.
        if (PortraitNodes.FindControl(layout, "FakeNextButtonContainer") is { } fakeNext)
            fakeNext.GlobalPosition = new Vector2(
                fakeNext.GlobalPosition.X,
                top + dialogue.Size.Y + 12f
            );
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout), "_Ready")]
internal static class AncientEventReadyPatch
{
    private static void Postfix(object __instance)
    {
        var layout = (Control)__instance;
        PortraitNodes.After(layout, 0.25, () => PortraitAncientEvent.EnsureLoop(layout));
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout), "SetDialogueLineAndAnimate")]
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

[HarmonyPatch(typeof(NProceedButton), "ShowPos", MethodType.Getter)]
internal static class ProceedButtonPatch
{
    private static void Postfix(object __instance, ref Vector2 __result)
    {
        var canvas = PortraitDisplay.CanvasSize;
        if (!PortraitDisplay.IsPortrait(canvas))
            return;
        var button = (Control)__instance;
        var width = button.Size.X > 1f ? button.Size.X : 300f;
        __result.X = Math.Min(__result.X, canvas.X - width - 20f);
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
    }
}
