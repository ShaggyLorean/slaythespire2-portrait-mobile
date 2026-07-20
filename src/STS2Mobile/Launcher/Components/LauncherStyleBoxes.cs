using Godot;

namespace STS2Mobile.Launcher.Components;

internal static class LauncherStyleBoxes
{
    internal static StyleBoxFlat MakeFilled(Color bg, int cornerRadius)
    {
        var style = new StyleBoxFlat();
        style.BgColor = bg;
        style.SetCornerRadiusAll(cornerRadius);
        return style;
    }

    internal static StyleBoxFlat MakeOutline(Color borderColor, int cornerRadius, int borderWidth)
    {
        var style = new StyleBoxFlat();
        style.BgColor = Colors.Transparent;
        style.BorderColor = borderColor;
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(cornerRadius);
        return style;
    }

    internal static StyleBoxFlat MakeFramed(
        Color background,
        Color border,
        int cornerRadius,
        int borderWidth,
        int horizontalPadding,
        int verticalPadding
    )
    {
        var style = MakeFilled(background, cornerRadius);
        style.BorderColor = border;
        style.SetBorderWidthAll(borderWidth);
        style.ContentMarginLeft = horizontalPadding;
        style.ContentMarginRight = horizontalPadding;
        style.ContentMarginTop = verticalPadding;
        style.ContentMarginBottom = verticalPadding;
        return style;
    }
}
