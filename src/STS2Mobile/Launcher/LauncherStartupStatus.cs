using System;
using Godot;
using STS2Mobile.Patches;
using STS2Mobile.Portrait;

namespace STS2Mobile.Launcher;

internal static class LauncherStartupStatus
{
    private const int FontSize = 34;
    private const string NodeName = "STS2MobileStartupStatus";
    private const int ZIndex = 4096;

    internal static Label CreateLabel(Node parent)
    {
        try
        {
            var label = new Label
            {
                Name = NodeName,
                ZIndex = ZIndex,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.AnchorRight = 1f;
            label.OffsetLeft = 36f;
            label.OffsetTop = PortraitDisplay.SafeTop() + 24f;
            label.OffsetRight = -36f;
            label.CustomMinimumSize = new Vector2(0f, 72f);
            label.AddThemeFontSizeOverride("font_size", FontSize);
            label.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 1f));
            parent.AddChild(label);
            return label;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Startup status label creation failed: {ex.Message}");
            return null;
        }
    }

    internal static void Set(Label label, string message)
    {
        PatchHelper.Log($"[Startup] {message}");
        if (label == null)
            return;

        try
        {
            label.Text = message;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Startup status label update failed: {ex.Message}");
        }
    }

}
