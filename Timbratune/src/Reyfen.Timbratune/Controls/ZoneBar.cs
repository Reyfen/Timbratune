using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// Port of ZoneBar.tsx: coloured zone segments over [Lo, Hi] with a marker
/// at Value, and the Lo / Hi labels underneath.
/// </summary>
public sealed class ZoneBar : ThemedControl
{
    public static readonly StyledProperty<IReadOnlyList<Zone>?> ZonesProperty =
        AvaloniaProperty.Register<ZoneBar, IReadOnlyList<Zone>?>(nameof(Zones));
    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<ZoneBar, double?>(nameof(Value));
    public static readonly StyledProperty<double> LoProperty =
        AvaloniaProperty.Register<ZoneBar, double>(nameof(Lo));
    public static readonly StyledProperty<double> HiProperty =
        AvaloniaProperty.Register<ZoneBar, double>(nameof(Hi), 1);

    static ZoneBar() => RedrawOn<ZoneBar>(ZonesProperty, ValueProperty, LoProperty, HiProperty);

    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }
    public double? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Lo { get => GetValue(LoProperty); set => SetValue(LoProperty, value); }
    public double Hi { get => GetValue(HiProperty); set => SetValue(HiProperty, value); }

    private const double BarHeight = 12;
    private const double LabelHeight = 16;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, BarHeight + 6 + LabelHeight);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var span = Hi - Lo;
        if (w <= 0 || span <= 0) return;
        var barTop = 3.0;

        using (ctx.PushClip(new RoundedRect(new Rect(0, barTop, w, BarHeight), BarHeight / 2)))
        {
            ctx.FillRectangle(B("LineSoft"), new Rect(0, barTop, w, BarHeight));
            foreach (var z in Zones ?? [])
            {
                var left = Math.Max(0, (z.From - Lo) / span) * w;
                var width = (Math.Min(Hi, z.To) - Math.Max(Lo, z.From)) / span * w;
                if (width > 0) ctx.FillRectangle(new SolidColorBrush(ZoneColor(z.Color)), new Rect(left, barTop, width, BarHeight));
            }
        }

        if (Value is { } v)
        {
            var x = Math.Clamp((v - Lo) / span, 0, 1) * w;
            var marker = new Rect(Math.Clamp(x - 3, 0, w - 6), barTop - 3, 6, BarHeight + 6);
            ctx.DrawRectangle(B("Card"), new Pen(B("InkStrong"), 2), new RoundedRect(marker, 3));
        }

        var ink = C("InkSoft");
        var lo = Text(Lo.ToString(CultureInfo.InvariantCulture), 11, ink);
        var hi = Text(Hi.ToString(CultureInfo.InvariantCulture), 11, ink);
        var y = barTop + BarHeight + 4;
        ctx.DrawText(lo, new Point(0, y));
        ctx.DrawText(hi, new Point(w - hi.Width, y));
    }
}
