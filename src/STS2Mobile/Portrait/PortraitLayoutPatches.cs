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

internal sealed class PortraitTopVignette : Control
{
    public override void _Draw()
    {
        var width = Math.Max(1f, Size.X);
        var height = Math.Max(1f, Size.Y);
        DrawPolygon(
            new[]
            {
                Vector2.Zero,
                new Vector2(width, 0f),
                new Vector2(width, height),
                new Vector2(0f, height),
            },
            new[]
            {
                new Color(0.018f, 0.038f, 0.030f, 0.98f),
                new Color(0.018f, 0.038f, 0.030f, 0.98f),
                new Color(0.018f, 0.038f, 0.030f, 0f),
                new Color(0.018f, 0.038f, 0.030f, 0f),
            }
        );
    }
}

internal sealed class PortraitCombatFrame : Control
{
    private Vector2 _configuredSize;

    internal void Configure(Vector2 canvas)
    {
        if (_configuredSize == canvas && GetChildCount() > 0)
            return;

        _configuredSize = canvas;
        foreach (var child in GetChildren())
            child.QueueFree();

        var width = Math.Max(1f, canvas.X);
        var height = Math.Max(1f, canvas.Y);
        const float topSolid = 118f;
        const float topFade = 430f;
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

    internal static void PlaceHand(Control holder, Vector2 canvas)
    {
        holder.Scale = Vector2.One * HandScaleFor(holder);
        holder.Position = new Vector2(holder.Position.X, canvas.Y * HandBaseline);
        holder.ZAsRelative = false;
        holder.ZIndex = 320;
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
                if (GodotObject.IsInstanceValid(frame))
                    frame.QueueFree();
                if (OperatingSystem.IsAndroid())
                {
                    AndroidGodotAppBridge.ClearStatusBarColor();
                    AndroidGodotAppBridge.HideCombatTopCover();
                }
            };
        }

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
    private const string VignetteName = "Sts2PortraitTopVignette";

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

    private static void EnsureVignette(NTopBar bar, Vector2 canvas)
    {
        var vignette = bar.GetNodeOrNull<PortraitTopVignette>(VignetteName);
        if (vignette is null)
        {
            vignette = new PortraitTopVignette
            {
                Name = VignetteName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            bar.AddChild(vignette);
            bar.MoveChild(vignette, 0);
        }

        PortraitNodes.ClearAnchors(vignette);
        vignette.Position = Vector2.Zero;
        vignette.Size = new Vector2(canvas.X, 520f);
        vignette.QueueRedraw();
    }

    private static bool IsUpperRightBuildText(Control control, string text, Vector2 canvas)
        => !string.IsNullOrWhiteSpace(text)
            && control.GlobalPosition.X > canvas.X * 0.62f
            && control.GlobalPosition.Y < PortraitDisplay.SafeTop() + 4f;

    private static void HideBuildWatermark(Node root, Vector2 canvas)
    {
        if (root is Label label)
        {
            var text = label.Text ?? "";
            if (
                root.Name.ToString().Contains("Build", StringComparison.OrdinalIgnoreCase)
                || text.Equals("NONE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("???", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("CI", StringComparison.OrdinalIgnoreCase)
                || text.Contains("[NONE]", StringComparison.OrdinalIgnoreCase)
                || text.Contains("(???)", StringComparison.OrdinalIgnoreCase)
                || IsUpperRightBuildText(label, text, canvas)
            )
                label.Visible = false;
        }
        else if (root is RichTextLabel richText)
        {
            var text = richText.Text ?? "";
            if (
                root.Name.ToString().Contains("Build", StringComparison.OrdinalIgnoreCase)
                || text.Equals("NONE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("???", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("CI", StringComparison.OrdinalIgnoreCase)
                || text.Contains("[NONE]", StringComparison.OrdinalIgnoreCase)
                || text.Contains("(???)", StringComparison.OrdinalIgnoreCase)
                || IsUpperRightBuildText(richText, text, canvas)
            )
                richText.Visible = false;
        }
        else if (root is CanvasItem canvasItem && root.GetType().Name == "NDebugInfoLabelManager")
        {
            canvasItem.Visible = false;
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
        var left = bar.GetNodeOrNull<Control>("LeftAlignedStuff");
        var right = bar.GetNodeOrNull<Control>("RightAlignedStuff");

        // Do not scale the two desktop rows as single 1280px/640px strips. That
        // made every icon and number microscopic. Reset their transforms and lay
        // out the actual controls as a compact portrait HUD instead.
        if (left is not null)
        {
            PortraitNodes.ClearAnchors(left);
            left.PivotOffset = Vector2.Zero;
            left.Scale = Vector2.One;
            left.Position = Vector2.Zero;
        }

        if (right is not null)
        {
            PortraitNodes.ClearAnchors(right);
            right.PivotOffset = Vector2.Zero;
            right.Scale = Vector2.One;
            right.Position = Vector2.Zero;
        }

        var top = safeTop + 24f;
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

        Place(hp, new Vector2(38f, top), 1.28f);
        Place(gold, new Vector2(38f, top + 92f), 1.28f);
        Place(potions, new Vector2(38f, top + 184f), 1.25f);
        Place(room, new Vector2(38f, top + 286f), 1.32f);
        Place(floor, new Vector2(168f, top + 286f), 1.32f);
        Place(boss, new Vector2(322f, top + 286f), 1.32f);

        var rightEdge = canvas.X - 38f;
        rightEdge = PlaceFromRight(pause, rightEdge, top, 1.50f);
        rightEdge = PlaceFromRight(deck, rightEdge, top, 1.50f);
        PlaceFromRight(map, rightEdge, top, 1.50f);
        if (timer is not null)
            timer.Visible = false;

        var parent = bar.GetParent();
        var relics = parent is null ? null : PortraitNodes.FindControl(parent, "RelicInventory");
        if (relics is not null)
        {
            relics.ZAsRelative = false;
            relics.ZIndex = 410;
            var count = 0;
            foreach (var child in relics.GetChildren())
            {
                if (child is CanvasItem { Visible: true })
                    count++;
            }

            var contentWidth = Math.Max(72f, 14f + count * 68f);
            var maxWidth = canvas.X * 0.78f;
            var scale = Mathf.Min(1.48f, maxWidth / contentWidth);
            relics.PivotOffset = Vector2.Zero;
            relics.Scale = Vector2.One * scale;
            relics.Position += new Vector2(38f, top + 394f) - relics.GlobalPosition;
        }

        HideBuildWatermark(bar.GetTree().Root, canvas);

        var signature = $"portrait-zones-5:{canvas.X:F0}:{relics?.GetChildCount() ?? 0}";
        if (_lastSignature != signature)
        {
            _lastSignature = signature;
            PatchHelper.Log($"[Portrait] Top bar reflow {signature}");
        }
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
        var delay = pass < 8 ? 0.35 : 1.2;
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

            var title = PortraitNodes.FindControl(room, "Title");
            if (title?.GetParent() is not Control block)
                return;
            var width = title.Size.X > 1f ? title.Size.X : Math.Min(800f, canvas.X - 80f);
            block.GlobalPosition = new Vector2((canvas.X - width) * 0.5f, block.GlobalPosition.Y);
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

        var relics = PortraitNodes.FindControl(slots, "Relics");
        var potions = PortraitNodes.FindControl(slots, "Potions");
        var bandY = panelHeight - utilityBand + 35f;
        if (relics is not null)
            Place(relics, origin + new Vector2(panelWidth * 0.28f, bandY), 0.95f);
        if (potions is not null)
            Place(potions, origin + new Vector2(panelWidth * 0.70f, bandY), 0.95f);
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
        if (!inventory.HasMeta("sts2_portrait_shop_closed"))
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
