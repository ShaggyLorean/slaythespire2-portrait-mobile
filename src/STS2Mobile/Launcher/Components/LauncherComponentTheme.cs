using Godot;

namespace STS2Mobile.Launcher.Components;

internal static class LauncherComponentTheme
{
    internal const int ButtonDefaultFontSize = 19;
    internal const int ButtonDefaultHeight = 68;
    internal const int ButtonRadius = 0;
    internal const int DialogButtonFontSize = 14;
    internal const int DialogButtonHeight = 44;
    internal const int DialogButtonWidth = 120;
    internal const int DialogButtonSeparation = 12;
    internal const int DialogContentSeparation = 16;
    internal const int DialogMessageFontSize = 16;
    internal const int DialogMessageWidth = 300;
    internal const int DialogPanelMargin = 24;
    internal const int DialogPanelRadius = 0;
    internal const int LineEditFontSize = 14;
    internal const int LineEditHeight = 64;
    internal const int LogFontSize = 12;
    internal const int LogHeight = 56;
    internal const int LogMarginHorizontal = 8;
    internal const int LogMarginVertical = 4;
    internal const int LogRadius = 0;
    internal const int PanelBottomMargin = 24;
    internal const int PanelHorizontalMargin = 24;
    internal const int PanelRadius = 0;
    internal const int PanelTopMargin = 24;
    internal const int ProgressBarHeight = 24;
    internal const string FontSize = "font_size";
    internal const string DefaultColor = "default_color";
    internal const string NormalFontSize = "normal_font_size";
    internal const string Panel = "panel";
    internal const string StateDisabled = "disabled";
    internal const string StateHover = "hover";
    internal const string StateNormal = "normal";
    internal const string StatePressed = "pressed";
    internal const string ThemeSeparation = "separation";

    internal static readonly Color ButtonDisabled = new("2b2f38");
    internal static readonly Color ButtonHover = new("343a45");
    internal static readonly Color ButtonNormal = new("242932");
    internal static readonly Color ButtonPressed = new("8f3138");
    internal static readonly Color DialogOverlay = new(0, 0, 0, 0.6f);
    internal static readonly Color DialogPanelBackground = new("242932");
    internal static readonly Color LogBackground = new("1b1f26");
    internal static readonly Color LogText = new("9aa3b0");
    internal static readonly Color PanelBackground = new("181b21");
    internal static readonly Color ScreenBackground = new("181b21");
    internal static readonly Color Gold = new("f0b73f");
    internal static readonly Color GoldDim = new("6d5a30");
    internal static readonly Color Ivory = new("f4f2ee");
    internal static readonly Color MutedIvory = new("a7aeba");
    internal static readonly Color SpireCyan = new("54c8d8");
    internal static readonly Color SpireCrimson = new("b23b41");
    internal static readonly Color Parchment = new("e8d7a8");
    internal static readonly Color ParchmentShade = new("c2ad7c");
    internal static readonly Color ParchmentInk = new("1b1207");
    internal static readonly Color InkRule = new("11141a");
    internal static readonly Color SlabRule = new("39404c");

    internal static readonly Font DisplayFont = CreateSystemFont(
        "NotoSerif-Bold",
        "Noto Serif",
        "Noto Serif Display",
        "Droid Serif",
        "serif"
    );

    internal static readonly Font BodyFont = CreateSystemFont(
        "Noto Sans",
        "Roboto",
        "Droid Sans",
        "sans-serif"
    );

    internal static int ScaleInt(float scale, int value)
        => (int)(value * scale);

    private static Font CreateSystemFont(params string[] names)
        => new SystemFont { FontNames = names };
}
