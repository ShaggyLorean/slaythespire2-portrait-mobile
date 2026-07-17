using Godot;

namespace Sts2Portrait;

/// <summary>
/// Portrait yerleşiminin tüm ayar/magic-number'ları — balatro-portrait-mobile'daki
/// portrait_config.lua'nın C# karşılığı. Tüm patch'ler değerleri buradan okur.
/// </summary>
public static class PortraitConfig
{
    /// <summary>
    /// Dikey modda hedef canvas genişliği. canvas_items + expand stretch ile
    /// efektif canvas = (BaseWidth, BaseWidth * pencere_oranı) olur.
    /// Oyunun tasarım yüksekliği 1080 olduğundan 1080 genişlik, UI elemanlarını
    /// tasarım ölçeğinin ~%100'ünde tutar (1920 yerine).
    /// </summary>
    public const int CanvasBaseWidth = 1080;

    /// <summary>PC'de test için açılış pencere boyutu. Ekrana sığar, viewport capture için iyi çözünürlük.</summary>
    public static readonly Vector2I DevWindowSize = new(720, 1440);

    /// <summary>Pencere gerçekten dikey mi? (portrait patch'leri sadece bu durumda uygulanır)</summary>
    public static bool IsPortrait(Vector2 windowSize) => windowSize.Y > windowSize.X;

    /// <summary>Aspect oranına göre adaptif genel ölçek (balatro'daki 0.58–0.80 dinamiği gibi).</summary>
    public static float GetAdaptiveScale(Vector2 windowSize)
    {
        if (windowSize.X <= 0) return 1f;
        float aspect = windowSize.Y / windowSize.X; // dikeyde > 1
        // 4:3 dikey (1.33) → 1.0, 9:16 (1.78) → 1.0, 9:21 (2.33) → 0.92 gibi
        return aspect > 2.0f ? 0.92f : 1.0f;
    }

    /// <summary>Üst güvenli alan (punch-hole/çentik) — canvas birimi. Android'de runtime'da güncellenir.</summary>
    public static float SafeAreaTop = 40f;

    /// <summary>Alt güvenli alan (gesture bar) — canvas birimi.</summary>
    public static float SafeAreaBottom = 30f;

    /// <summary>Aktif dikey canvas boyutu (ContentScaleSize).</summary>
    public static Vector2 CanvasSize =>
        Engine.GetMainLoop() is SceneTree t ? (Vector2)t.Root.ContentScaleSize : new Vector2(1173, 2541);

    public static Vector2 CanvasCenter => CanvasSize * 0.5f;
}
