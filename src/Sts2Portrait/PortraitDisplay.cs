using Godot;

namespace Sts2Portrait;

/// <summary>
/// Pencere/canvas ölçek yönetimi. Dikey modda ContentScaleSize'ı
/// (CanvasBaseWidth, oranla uzayan yükseklik) yapar ve PC'de geliştirme için
/// pencereyi dikey boyuta küçültür. Android'de pencere zaten dikey olduğundan
/// yalnızca ContentScaleSize devreye girer.
/// </summary>
public static class PortraitDisplay
{
    private static bool _devWindowApplied;

    /// <summary>Şu anki ana pencere (SceneTree kökü).</summary>
    public static Window? MainWindow =>
        Engine.GetMainLoop() is SceneTree tree ? tree.Root : null;

    /// <summary>Dikey canvas ölçeğini uygula. Oyunun display ayarları çalıştıktan sonra çağrılır.</summary>
    private static bool _orientationApplied;

    public static void ApplyPortrait()
    {
        var window = MainWindow;
        if (window is null)
            return;

        // ANDROID: manifest-only orientation Godot'ta çalışmaz (oyun runtime'da project
        // ayarından yönü landscape'e geri set eder). Mod'dan runtime çağrısı bunu ezer →
        // tüm activity portrait'e döner. PC'de zararsız (masaüstü pencere yönü etkilenmez).
        if (!_orientationApplied && !OS.HasFeature("pc"))
        {
            _orientationApplied = true;
            try { DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait); }
            catch (System.Exception e) { PortraitMod.Log($"orientation set failed: {e.Message}"); }
            PortraitMod.Log("android orientation -> Portrait");
        }

        // PC geliştirme: fullscreen yatay pencereyi ilk seferde dikey pencereye çevir.
        if (!_devWindowApplied && OS.HasFeature("pc"))
        {
            _devWindowApplied = true;
            window.Mode = Window.ModeEnum.Windowed;
            window.Size = PortraitConfig.DevWindowSize;
            // pencereyi ekranda ortala
            var screen = DisplayServer.ScreenGetSize();
            window.Position = (screen - PortraitConfig.DevWindowSize) / 2;
            PortraitMod.Log($"dev window -> windowed {PortraitConfig.DevWindowSize}");
        }

        var winSize = (Vector2)window.Size;
        if (!PortraitConfig.IsPortrait(winSize))
        {
            PortraitMod.Log($"window not portrait ({winSize}); skipping canvas override");
            return;
        }

        float aspect = winSize.Y / winSize.X;
        float scale = PortraitConfig.GetAdaptiveScale(winSize);
        int baseWidth = (int)(PortraitConfig.CanvasBaseWidth / scale);

        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleSize = new Vector2I(baseWidth, (int)(baseWidth * aspect));

        PortraitMod.Log(
            $"portrait canvas: window={winSize} aspect={aspect:F3} scale={scale:F2} " +
            $"contentScaleSize={window.ContentScaleSize}");
    }
}
