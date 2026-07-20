using Godot;

namespace STS2Mobile.Launcher.Components;

internal static class LauncherComponentTheme
{
    internal const int ButtonDefaultFontSize = 16;
    internal const int ButtonDefaultHeight = 56;
    internal const int ButtonRadius = 4;
    internal const int DialogButtonFontSize = 14;
    internal const int DialogButtonHeight = 44;
    internal const int DialogButtonWidth = 120;
    internal const int DialogButtonSeparation = 12;
    internal const int DialogContentSeparation = 16;
    internal const int DialogMessageFontSize = 16;
    internal const int DialogMessageWidth = 300;
    internal const int DialogPanelMargin = 24;
    internal const int DialogPanelRadius = 8;
    internal const int LineEditFontSize = 14;
    internal const int LineEditHeight = 56;
    internal const int LogFontSize = 11;
    internal const int LogHeight = 56;
    internal const int LogMarginHorizontal = 8;
    internal const int LogMarginVertical = 4;
    internal const int LogRadius = 4;
    internal const int PanelBottomMargin = 24;
    internal const int PanelHorizontalMargin = 24;
    internal const int PanelRadius = 18;
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

    internal static readonly Color ButtonDisabled = new("20242b");
    internal static readonly Color ButtonHover = new("473042");
    internal static readonly Color ButtonNormal = new("172536");
    internal static readonly Color ButtonPressed = new("681f2b");
    internal static readonly Color DialogOverlay = new(0, 0, 0, 0.6f);
    internal static readonly Color DialogPanelBackground = new("111a28");
    internal static readonly Color LogBackground = new("070b12");
    internal static readonly Color LogText = new("b8b0a1");
    internal static readonly Color PanelBackground = new(0.035f, 0.060f, 0.095f, 0.92f);
    internal static readonly Color ScreenBackground = new("070b13");
    internal static readonly Color Gold = new("e4b85a");
    internal static readonly Color GoldDim = new("806632");
    internal static readonly Color Ivory = new("eee5cf");
    internal static readonly Color MutedIvory = new("bdb4a3");
    internal static readonly Color SpireCyan = new("55c7d7");
    internal static readonly Color SpireCrimson = new("7f2633");

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
