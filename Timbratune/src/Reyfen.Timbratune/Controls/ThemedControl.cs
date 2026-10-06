using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// Base for the custom-drawn charts: resolves theme colour tokens
/// (Themes/Palette.axaml) and repaints when the light/dark variant flips.
/// </summary>
public abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected Color C(string key, Color? fallback = null) =>
        this.TryFindResource(key + "Color", ActualThemeVariant, out var v) && v is Color c
            ? c
            : fallback ?? Colors.Magenta;

    protected IBrush B(string key, double opacity = 1) =>
        new SolidColorBrush(C(key), opacity);

    protected Color ZoneColor(ZoneColorKey key) => C(ZoneKey(key));

    public static string ZoneKey(ZoneColorKey key) => key switch
    {
        ZoneColorKey.Masc => "ZoneMasc",
        ZoneColorKey.Fem => "ZoneFem",
        ZoneColorKey.Neutral => "ZoneNeutral",
        ZoneColorKey.Grow => "ZoneGrow",
        ZoneColorKey.Soft => "ZoneSoft",
        ZoneColorKey.Comfy => "ZoneComfy",
        ZoneColorKey.Strong => "ZoneStrong",
        _ => "ZoneGrow",
    };

    protected FormattedText Text(string text, double size, Color color, FontWeight weight = FontWeight.Normal) =>
        new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, new SolidColorBrush(color));

    /// <summary>Repaint whenever any of the given properties change.</summary>
    protected static void RedrawOn<T>(params AvaloniaProperty[] properties) where T : ThemedControl =>
        AffectsRender<T>(properties);
}
