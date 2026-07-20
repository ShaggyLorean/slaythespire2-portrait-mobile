using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal sealed class StyledButton : Button
{
    private readonly float _scale;

    internal StyledButton(
        string text,
        float scale,
        int fontSize = LauncherComponentTheme.ButtonDefaultFontSize,
        int height = LauncherComponentTheme.ButtonDefaultHeight
    )
    {
        _scale = scale;
        Text = text;
        CustomMinimumSize = new Vector2(0, LauncherComponentTheme.ScaleInt(scale, height));
        AddThemeFontSizeOverride(
            LauncherComponentTheme.FontSize,
            LauncherComponentTheme.ScaleInt(scale, fontSize)
        );
        AddThemeFontOverride("font", LauncherComponentTheme.BodyFont);
        AddThemeColorOverride("font_color", LauncherComponentTheme.Ivory);
        AddThemeColorOverride("font_hover_color", Colors.White);
        AddThemeColorOverride("font_pressed_color", LauncherComponentTheme.Gold);
        AddThemeColorOverride("font_disabled_color", LauncherComponentTheme.MutedIvory.Darkened(0.35f));
        ApplyTheme(scale);
    }

    internal void UsePrimaryAccent()
    {
        var radius = LauncherComponentTheme.ScaleInt(_scale, LauncherComponentTheme.ButtonRadius);
        var border = Math.Max(2, LauncherComponentTheme.ScaleInt(_scale, 1));
        ApplyState(
            LauncherComponentTheme.StateNormal,
            LauncherComponentTheme.SpireCrimson,
            LauncherComponentTheme.Gold,
            radius,
            border
        );
        ApplyState(
            LauncherComponentTheme.StateHover,
            LauncherComponentTheme.SpireCrimson.Lightened(0.12f),
            LauncherComponentTheme.SpireCyan,
            radius,
            border
        );
        ApplyState(
            LauncherComponentTheme.StatePressed,
            LauncherComponentTheme.SpireCrimson.Darkened(0.18f),
            LauncherComponentTheme.Gold,
            radius,
            border
        );
        AddThemeColorOverride("font_color", LauncherComponentTheme.Ivory);
    }

    private void ApplyTheme(float scale)
    {
        var radius = LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.ButtonRadius);
        var border = Math.Max(2, LauncherComponentTheme.ScaleInt(scale, 1));
        ApplyState(LauncherComponentTheme.StateNormal, LauncherComponentTheme.ButtonNormal, LauncherComponentTheme.GoldDim, radius, border);
        ApplyState(LauncherComponentTheme.StateHover, LauncherComponentTheme.ButtonHover, LauncherComponentTheme.Gold, radius, border);
        ApplyState(LauncherComponentTheme.StatePressed, LauncherComponentTheme.ButtonPressed, LauncherComponentTheme.SpireCyan, radius, border);
        ApplyState(LauncherComponentTheme.StateDisabled, LauncherComponentTheme.ButtonDisabled, LauncherComponentTheme.GoldDim.Darkened(0.35f), radius, border);
    }

    private void ApplyState(string state, Color background, Color borderColor, int radius, int border)
        => AddThemeStyleboxOverride(
            state,
            LauncherStyleBoxes.MakeFramed(
                background,
                borderColor,
                radius,
                border,
                LauncherComponentTheme.ScaleInt(_scale, 12),
                LauncherComponentTheme.ScaleInt(_scale, 5)
            )
        );
}
