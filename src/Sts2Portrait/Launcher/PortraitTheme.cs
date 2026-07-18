using Godot;

namespace Sts2Portrait.Launcher;

// Shared look for the screens we own. The game ships its own fonts inside the pck
// (Kreon for UI, Source Code Pro for monospace) and the pck is already mounted by the
// time our screens build, so we borrow them. That one change is what makes the shell
// read as part of the game instead of a stock engine UI.
public static class PortraitTheme
{
    public static readonly Color Bg = new("0d0807");
    public static readonly Color Gold = new("d8b24a");
    public static readonly Color GoldDim = new("6d5624");
    public static readonly Color Body = new("cfc4b0");
    public static readonly Color Muted = new("7c7264");
    public static readonly Color PanelFill = new("1c1210");
    public static readonly Color ButtonFill = new("241610");
    public static readonly Color ButtonFillHot = new("362214");

    private static FontFile _display, _displayBold, _mono;
    private static bool _fontsTried;

    private static void LoadFonts()
    {
        if (_fontsTried) return;
        _fontsTried = true;
        _displayBold = TryFont("res://fonts/kreon_bold.ttf");
        _display = TryFont("res://fonts/kreon_regular.ttf") ?? _displayBold;
        _mono = TryFont("res://fonts/source_code_pro_medium.ttf");
    }

    private static FontFile TryFont(string path)
    {
        try { return ResourceLoader.Exists(path) ? ResourceLoader.Load<FontFile>(path) : null; }
        catch { return null; }
    }

    public static Control Background()
    {
        LoadFonts();
        var bg = new ColorRect { Color = Bg };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var frame = new Panel();
        var box = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = GoldDim };
        box.SetBorderWidthAll(2);
        frame.AddThemeStyleboxOverride("panel", box);
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        frame.OffsetLeft = 26; frame.OffsetTop = 26; frame.OffsetRight = -26; frame.OffsetBottom = -26;
        frame.MouseFilter = Control.MouseFilterEnum.Ignore;
        bg.AddChild(frame);
        return bg;
    }

    public static Label Heading(string text, int size, Color? color = null, bool bold = true)
    {
        LoadFonts();
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        var f = bold ? _displayBold : _display;
        if (f is not null) l.AddThemeFontOverride("font", f);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Gold);
        return l;
    }

    public static Label BodyText(string text, int size = 34, Color? color = null)
    {
        LoadFonts();
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        if (_display is not null) l.AddThemeFontOverride("font", _display);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Body);
        return l;
    }

    public static Label MonoText(string text, int size = 24)
    {
        LoadFonts();
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        if (_mono is not null) l.AddThemeFontOverride("font", _mono);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Body);
        return l;
    }

    public static Control Rule(float width)
    {
        var wrap = new CenterContainer();
        var line = new ColorRect { Color = GoldDim, CustomMinimumSize = new Vector2(width, 2) };
        wrap.AddChild(line);
        return wrap;
    }

    public static void StyleButton(Button b, bool primary = true)
    {
        LoadFonts();
        if (_displayBold is not null) b.AddThemeFontOverride("font", _displayBold);
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", Box(ButtonFill));
            b.AddThemeStyleboxOverride("hover", Box(ButtonFillHot));
            b.AddThemeStyleboxOverride("pressed", Box(ButtonFillHot));
            b.AddThemeStyleboxOverride("disabled", Box(PanelFill));
        }
        else
        {
            var flat = new StyleBoxEmpty();
            b.AddThemeStyleboxOverride("normal", flat);
            b.AddThemeStyleboxOverride("hover", flat);
            b.AddThemeStyleboxOverride("pressed", flat);
            b.AddThemeStyleboxOverride("disabled", flat);
        }
        b.AddThemeColorOverride("font_color", primary ? new Color("f0d98a") : Muted);
        b.AddThemeColorOverride("font_hover_color", new Color("f0d98a"));
        b.AddThemeColorOverride("font_pressed_color", Gold);
        b.AddThemeColorOverride("font_disabled_color", Muted);
    }

    public static void StyleProgress(ProgressBar p)
    {
        p.AddThemeStyleboxOverride("background", Box(PanelFill, border: false, radius: 2));
        p.AddThemeStyleboxOverride("fill", Box(Gold, border: false, radius: 2));
    }

    public static StyleBoxFlat Box(Color fill, bool border = true, int radius = 4)
    {
        var s = new StyleBoxFlat { BgColor = fill };
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(20);
        if (border)
        {
            s.BorderColor = Gold;
            s.SetBorderWidthAll(2);
        }
        return s;
    }
}
