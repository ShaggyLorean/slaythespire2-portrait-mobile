using Godot;

namespace Sts2Portrait.Launcher;

// Shared look for every screen we own (launcher, shader warmup). One place to keep the
// palette so the whole shell feels like a single app instead of a reskinned fork.
public static class PortraitTheme
{
    public static readonly Color BgTop = new("1a0f0b");
    public static readonly Color BgBottom = new("070404");
    public static readonly Color Gold = new("d8b24a");
    public static readonly Color GoldDim = new("8a6f2e");
    public static readonly Color Teal = new("7fb0b8");
    public static readonly Color Body = new("cfc4b0");
    public static readonly Color Muted = new("857a6c");
    public static readonly Color PanelFill = new("221510");
    public static readonly Color ButtonFill = new("2e1d10");
    public static readonly Color ButtonFillHot = new("40281a");

    public static Control Background()
    {
        var grad = new Gradient();
        grad.SetColor(0, BgTop);
        grad.SetColor(1, BgBottom);
        var tex = new GradientTexture2D { Gradient = grad, FillFrom = new Vector2(0.5f, 0f), FillTo = new Vector2(0.5f, 1f) };
        var rect = new TextureRect { Texture = tex, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    public static Label Heading(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Gold);
        return l;
    }

    public static Label BodyText(string text, int size = 34)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Body);
        return l;
    }

    public static Control Divider(float width)
    {
        var wrap = new CenterContainer();
        var line = new ColorRect { Color = GoldDim, CustomMinimumSize = new Vector2(width, 3) };
        wrap.AddChild(line);
        return wrap;
    }

    public static void StyleButton(Button b)
    {
        var normal = Box(ButtonFill);
        var hot = Box(ButtonFillHot);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hot);
        b.AddThemeStyleboxOverride("pressed", hot);
        b.AddThemeStyleboxOverride("disabled", Box(PanelFill));
        b.AddThemeColorOverride("font_color", new Color("f0d98a"));
        b.AddThemeColorOverride("font_disabled_color", Muted);
    }

    public static void StyleProgress(ProgressBar p)
    {
        var bg = Box(PanelFill, border: false);
        var fill = Box(Gold, border: false);
        p.AddThemeStyleboxOverride("background", bg);
        p.AddThemeStyleboxOverride("fill", fill);
    }

    private static StyleBoxFlat Box(Color fill, bool border = true)
    {
        var s = new StyleBoxFlat { BgColor = fill };
        s.SetCornerRadiusAll(16);
        s.SetContentMarginAll(18);
        if (border)
        {
            s.BorderColor = Gold;
            s.SetBorderWidthAll(3);
        }
        return s;
    }
}
