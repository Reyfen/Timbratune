using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// Base for the custom-drawn charts: resolves theme colour tokens
/// (Themes/Palette.axaml), repaints when the light/dark variant flips, and
/// skips drawing while scrolled out of view (<see cref="IsOnScreen"/>).
/// </summary>
public abstract class ThemedControl : Control
{
    // Shaping text is one of the costliest parts of a chart redraw on a phone, and the
    // labels (axis values, seconds) repeat: each distinct label is shaped once.
    private static readonly Dictionary<(string, double, Color, FontWeight), FormattedText> s_texts = new();
    private readonly Dictionary<string, Color> _colors = new();

    protected ThemedControl()
    {
        ActualThemeVariantChanged += (_, _) =>
        {
            _colors.Clear();
            InvalidateVisual();
        };
        EffectiveViewportChanged += (_, e) =>
        {
            var visible = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0
                          && e.EffectiveViewport.Intersects(new Rect(Bounds.Size));
            if (visible == IsOnScreen) return;
            IsOnScreen = visible;
            if (visible) InvalidateVisual(); // draw what changed while it was out of view
        };
    }

    /// <summary>
    /// False while the control is scrolled out of view: subclasses skip drawing (and
    /// animating) then, and redraw once when it comes back. On a phone only two or three
    /// of the charts are visible at a time, and redrawing the rest made live recording stutter.
    /// </summary>
    public bool IsOnScreen { get; private set; } = true;

    protected Color C(string key, Color? fallback = null)
    {
        if (_colors.TryGetValue(key, out var cached)) return cached;
        var color = this.TryFindResource(key + "Color", ActualThemeVariant, out var v) && v is Color c ? c : fallback ?? Colors.Magenta;
        _colors[key] = color;
        return color;
    }

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

    /// <summary>A shaped label, reused across redraws and controls (UI thread only).</summary>
    protected FormattedText Text(string text, double size, Color color, FontWeight weight = FontWeight.Normal)
    {
        var key = (text, size, color, weight);
        if (s_texts.TryGetValue(key, out var shaped)) return shaped;
        if (s_texts.Count > 2000) s_texts.Clear(); // values that keep changing (e.g. trend dot labels) shouldn't pile up
        shaped = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, new SolidColorBrush(color));
        s_texts[key] = shaped;
        return shaped;
    }

    /// <summary>Repaint whenever any of the given properties change.</summary>
    protected static void RedrawOn<T>(params AvaloniaProperty[] properties) where T : ThemedControl =>
        AffectsRender<T>(properties);
}
