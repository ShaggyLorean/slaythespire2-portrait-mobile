using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal sealed class StyledPanel : CenterContainer
{
    private const float MaxWidth = 1080f;
    private const float MaxHeight = 1800f;

    internal VBoxContainer Content { get; }

    internal StyledPanel(
        float scale,
        float widthRatio = 0.7f,
        bool fullBleed = false,
        float safeTop = 0f,
        float safeBottom = 0f
    )
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var vpSize = new Vector2(1920, 1080); // fallback, overridden after AddChild
        var panelContainer = new PanelContainer();
        panelContainer.CustomMinimumSize = fullBleed
            ? vpSize
            : new Vector2(vpSize.X * widthRatio, 0);

        panelContainer.AddThemeStyleboxOverride(
            LauncherComponentTheme.Panel,
            BuildStyle(scale, fullBleed, safeTop, safeBottom)
        );
        AddChild(panelContainer);

        Content = new VBoxContainer();
        Content.ZIndex = 10;
        Content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Content.AddThemeConstantOverride("separation", (int)(10 * scale));
        panelContainer.AddChild(Content);

        // Defer viewport-based sizing until in tree
        _panelContainer = panelContainer;
        _widthRatio = widthRatio;
        _fullBleed = fullBleed;
    }

    internal PanelContainer Panel => _panelContainer;
    private readonly PanelContainer _panelContainer;
    private readonly float _widthRatio;
    private readonly bool _fullBleed;
    private ScreenBackground? _backdrop;

    internal void UpdateSizeFromViewport(Vector2 vpSize)
    {
        _panelContainer.CustomMinimumSize = _fullBleed
            ? vpSize
            : new Vector2(
                Math.Min(vpSize.X * _widthRatio, MaxWidth),
                Math.Min(vpSize.Y * 0.94f, MaxHeight)
            );

        if (_fullBleed && _backdrop is null)
        {
            _backdrop = new ScreenBackground(vpSize) { ZIndex = 0 };
            _panelContainer.AddChild(_backdrop);
            _panelContainer.MoveChild(_backdrop, 0);
        }
    }

    private static StyleBoxFlat BuildStyle(
        float scale,
        bool fullBleed,
        float safeTop,
        float safeBottom
    )
    {
        var style = LauncherStyleBoxes.MakeFilled(
            LauncherComponentTheme.PanelBackground,
            fullBleed
                ? 0
                : LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.PanelRadius)
        );
        if (!fullBleed)
        {
            style.BorderColor = LauncherComponentTheme.GoldDim;
            style.SetBorderWidthAll(Math.Max(2, LauncherComponentTheme.ScaleInt(scale, 1)));
        }
        style.ContentMarginLeft = LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.PanelHorizontalMargin);
        style.ContentMarginRight = LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.PanelHorizontalMargin);
        style.ContentMarginTop = Math.Max(
            LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.PanelTopMargin),
            safeTop
        );
        style.ContentMarginBottom = Math.Max(
            LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.PanelBottomMargin),
            safeBottom
        );
        return style;
    }
}
