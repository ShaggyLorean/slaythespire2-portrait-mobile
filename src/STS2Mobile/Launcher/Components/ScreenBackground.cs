using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal sealed class ScreenBackground : Control
{
    internal ScreenBackground(Vector2 viewportSize)
    {
        MouseFilter = MouseFilterEnum.Stop;
        Position = Vector2.Zero;
        Size = viewportSize;
        BuildBackdrop();
    }

    private void BuildBackdrop()
    {
        var width = Math.Max(1f, Size.X);
        var height = Math.Max(1f, Size.Y);
        var baseColor = new ColorRect
        {
            Color = new Color("07111f"),
            MouseFilter = MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
            Size = new Vector2(width, height),
            ZIndex = -10,
        };
        AddChild(baseColor);

        // The launcher is intentionally compact; use the remaining height as
        // atmosphere instead of leaving an empty app-settings canvas. This is
        // drawn from simple shapes so it is available before the game PCK has
        // been mounted and does not depend on proprietary textures.
        var glowCenter = new Vector2(width * 0.68f, height * 0.69f);
        AddPolygon(
            Circle(glowCenter, width * 0.56f, 40),
            new Color(0.31f, 0.12f, 0.14f, 0.10f)
        );
        AddPolygon(
            Circle(glowCenter, width * 0.38f, 40),
            new Color(0.85f, 0.43f, 0.12f, 0.055f)
        );

        AddPolygon(
            new Vector2[]
            {
                new(0f, height * 0.68f),
                new(width * 0.13f, height * 0.61f),
                new(width * 0.24f, height * 0.66f),
                new(width * 0.37f, height * 0.55f),
                new(width * 0.48f, height * 0.65f),
                new(width * 0.62f, height * 0.57f),
                new(width * 0.75f, height * 0.64f),
                new(width * 0.90f, height * 0.54f),
                new(width, height * 0.61f),
                new(width, height),
                new(0f, height),
            },
            new Color(0.035f, 0.058f, 0.085f, 0.92f)
        );

        AddPolygon(
            new Vector2[]
            {
                new(0f, height * 0.79f),
                new(width * 0.16f, height * 0.72f),
                new(width * 0.28f, height * 0.76f),
                new(width * 0.45f, height * 0.67f),
                new(width * 0.59f, height * 0.75f),
                new(width * 0.73f, height * 0.70f),
                new(width * 0.88f, height * 0.76f),
                new(width, height * 0.70f),
                new(width, height),
                new(0f, height),
            },
            new Color(0.018f, 0.027f, 0.042f, 0.98f)
        );

        // A restrained Spire silhouette gives the empty lower half a focal
        // point without adding another card, button, heading or explanation.
        var tower = new[]
        {
            new Vector2(width * 0.61f, height),
            new Vector2(width * 0.61f, height * 0.70f),
            new Vector2(width * 0.65f, height * 0.67f),
            new Vector2(width * 0.66f, height * 0.56f),
            new Vector2(width * 0.685f, height * 0.49f),
            new Vector2(width * 0.70f, height * 0.39f),
            new Vector2(width * 0.715f, height * 0.49f),
            new Vector2(width * 0.75f, height * 0.56f),
            new Vector2(width * 0.76f, height * 0.67f),
            new Vector2(width * 0.80f, height * 0.70f),
            new Vector2(width * 0.80f, height),
        };
        AddPolygon(tower, new Color(0.012f, 0.018f, 0.028f, 1f));
        AddLine(new[] { tower[5], tower[8] }, new Color(LauncherComponentTheme.Gold, 0.28f), 2f);

        foreach (var marker in new[]
        {
            new Vector2(0.685f, 0.59f),
            new Vector2(0.724f, 0.63f),
            new Vector2(0.672f, 0.68f),
            new Vector2(0.742f, 0.73f),
            new Vector2(0.688f, 0.78f),
            new Vector2(0.754f, 0.84f),
        })
        {
            AddPolygon(
                Circle(
                    new Vector2(width * marker.X, height * marker.Y),
                    Math.Max(2.5f, width * 0.004f),
                    12
                ),
                new Color(LauncherComponentTheme.SpireCyan, 0.52f)
            );
        }

        var mist = new Color(0.42f, 0.62f, 0.67f, 0.10f);
        AddLine(
            new[]
            {
                new Vector2(width * 0.08f, height * 0.74f),
                new Vector2(width * 0.55f, height * 0.74f),
            },
            mist, 3f
        );
        AddLine(
            new[]
            {
                new Vector2(width * 0.40f, height * 0.82f),
                new Vector2(width * 0.94f, height * 0.82f),
            },
            mist, 2f
        );

        var inset = Math.Max(8f, width * 0.018f);
        var gold = new Color(LauncherComponentTheme.Gold, 0.32f);
        var cyan = new Color(LauncherComponentTheme.SpireCyan, 0.16f);
        AddLine(new[] { new Vector2(inset, 0f), new Vector2(inset, height) }, gold, 2f);
        AddLine(
            new[] { new Vector2(width - inset, 0f), new Vector2(width - inset, height) },
            cyan,
            2f
        );
        GD.Print($"[LauncherBackdrop] built at {width:F0}x{height:F0}");
    }

    private void AddPolygon(Vector2[] points, Color color)
        => AddChild(new Polygon2D { Polygon = points, Color = color });

    private void AddLine(Vector2[] points, Color color, float width)
        => AddChild(new Line2D { Points = points, DefaultColor = color, Width = width });

    private static Vector2[] Circle(Vector2 center, float radius, int segments)
    {
        var points = new Vector2[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = Mathf.Tau * index / segments;
            points[index] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }
}
