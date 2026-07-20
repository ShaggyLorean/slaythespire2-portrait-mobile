using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

internal sealed class ModeSelectionSection : VBoxContainer
{
    internal event Action OfflinePressed;
    internal event Action SteamOnlinePressed;

    internal ModeSelectionSection(float scale)
    {
        AddThemeConstantOverride(
            LauncherViewLayoutMetrics.ThemeSeparation,
            LauncherViewLayoutMetrics.ScaleInt(12, scale)
        );

        var offline = new StyledButton("Play offline", scale, fontSize: 18, height: 62);
        offline.TooltipText = "Import and play files copied from your own PC. Steam and Cloud stay off.";
        offline.Pressed += () => OfflinePressed?.Invoke();
        AddChild(offline);

        var online = new StyledButton("Sign in with Steam", scale, fontSize: 18, height: 62);
        online.UsePrimaryAccent();
        online.TooltipText = "Sign in to Steam for ownership verification, downloads, and Cloud controls.";
        online.Pressed += () => SteamOnlinePressed?.Invoke();
        AddChild(online);
    }

    private static Label Description(string text, float scale)
    {
        var label = new StyledLabel(text, scale, fontSize: 15);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeColorOverride(
            LauncherViewLayoutMetrics.ThemeFontColor,
            LauncherViewLayoutMetrics.LogTitleColor
        );
        return label;
    }
}
