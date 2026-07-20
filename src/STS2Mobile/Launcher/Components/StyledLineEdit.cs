using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal sealed class StyledLineEdit : LineEdit
{
    internal StyledLineEdit(string placeholder, float scale, bool secret = false)
    {
        PlaceholderText = placeholder;
        Secret = secret;
        CustomMinimumSize = new Vector2(
            0,
            LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.LineEditHeight)
        );
        AddThemeFontSizeOverride(
            LauncherComponentTheme.FontSize,
            LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.LineEditFontSize)
        );
        AddThemeFontOverride("font", LauncherComponentTheme.BodyFont);
        AddThemeColorOverride("font_color", LauncherComponentTheme.Ivory);
        AddThemeColorOverride("font_placeholder_color", LauncherComponentTheme.MutedIvory);
        var radius = LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.ButtonRadius);
        var border = Math.Max(2, LauncherComponentTheme.ScaleInt(scale, 1));
        AddThemeStyleboxOverride(
            "normal",
            LauncherStyleBoxes.MakeFramed(
                LauncherComponentTheme.LogBackground,
                LauncherComponentTheme.GoldDim,
                radius,
                border,
                LauncherComponentTheme.ScaleInt(scale, 12),
                LauncherComponentTheme.ScaleInt(scale, 6)
            )
        );
        AddThemeStyleboxOverride(
            "focus",
            LauncherStyleBoxes.MakeFramed(
                LauncherComponentTheme.LogBackground.Lightened(0.04f),
                LauncherComponentTheme.SpireCyan,
                radius,
                border,
                LauncherComponentTheme.ScaleInt(scale, 12),
                LauncherComponentTheme.ScaleInt(scale, 6)
            )
        );
        ContextMenuEnabled = true;
        ShortcutKeysEnabled = true;
        SelectAllOnFocus = true;
        FocusEntered += ShowAndroidKeyboard;
        GuiInput += inputEvent =>
        {
            if (ShouldShowKeyboard(inputEvent))
                ShowAndroidKeyboard();
        };
    }

    private void ShowAndroidKeyboard()
    {
        try
        {
            DisplayServer.VirtualKeyboardShow(
                Text,
                new Rect2(GlobalPosition, Size),
                DisplayServer.VirtualKeyboardType.Default,
                MaxLength,
                CaretColumn,
                CaretColumn
            );
        }
        catch
        {
            // Some desktop/editor backends do not expose a virtual keyboard.
        }
    }

    private static bool ShouldShowKeyboard(InputEvent inputEvent)
        => inputEvent
            is InputEventMouseButton { Pressed: true }
                or InputEventScreenTouch { Pressed: true };
}
