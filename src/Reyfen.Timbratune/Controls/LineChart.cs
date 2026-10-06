using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

/// <summary>One point of a trend chart: its x-axis label (e.g. the phrase number) and value (null = not measured).</summary>
public readonly record struct TrendPoint(string Label, double? Y, string? Tooltip = null);

/// <summary>
/// Port of LineChart.tsx: a small trend line (here: phrase by phrase), with either one
/// highlighted band or multi-zone shaded backgrounds. Drawn in real pixels
/// (width follows the layout) instead of a scaled SVG viewBox.
/// </summary>
public sealed class LineChart : ThemedControl
{
    public static readonly StyledProperty<IReadOnlyList<TrendPoint>?> PointsProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<TrendPoint>?>(nameof(Points));
    /// <summary>Palette token name for the line, e.g. "Chart1".</summary>
    public static readonly StyledProperty<string> LineColorKeyProperty =
        AvaloniaProperty.Register<LineChart, string>(nameof(LineColorKey), "InkAccent");
    public static readonly StyledProperty<double?> BandFromProperty =
        AvaloniaProperty.Register<LineChart, double?>(nameof(BandFrom));
    public static readonly StyledProperty<double?> BandToProperty =
        AvaloniaProperty.Register<LineChart, double?>(nameof(BandTo));
    public static readonly StyledProperty<string> BandColorKeyProperty =
        AvaloniaProperty.Register<LineChart, string>(nameof(BandColorKey), "Accent");
    public static readonly StyledProperty<IReadOnlyList<Zone>?> BandsProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<Zone>?>(nameof(Bands));

    static LineChart() => RedrawOn<LineChart>(PointsProperty, LineColorKeyProperty, BandFromProperty,
        BandToProperty, BandColorKeyProperty, BandsProperty);

    public IReadOnlyList<TrendPoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public string LineColorKey { get => GetValue(LineColorKeyProperty); set => SetValue(LineColorKeyProperty, value); }
    public double? BandFrom { get => GetValue(BandFromProperty); set => SetValue(BandFromProperty, value); }
    public double? BandTo { get => GetValue(BandToProperty); set => SetValue(BandToProperty, value); }
    public string BandColorKey { get => GetValue(BandColorKeyProperty); set => SetValue(BandColorKeyProperty, value); }
    public IReadOnlyList<Zone>? Bands { get => GetValue(BandsProperty); set => SetValue(BandsProperty, value); }

    private const double H = 150;
    private const double PadL = 36, PadR = 12, PadT = 12, PadB = 24;
    private readonly List<(Point At, TrendPoint P)> _dots = [];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, H);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var hit = _dots.Where(d => Math.Abs(d.At.X - pos.X) < 10 && Math.Abs(d.At.Y - pos.Y) < 10)
            .Select(d => (TrendPoint?)d.P).FirstOrDefault();
        ToolTip.SetTip(this, hit is { } p ? p.Tooltip ?? $"{p.Label}: {Zones.Fmt(p.Y)}" : null);
    }

    public override void Render(DrawingContext ctx)
    {
        _dots.Clear();
        var points = Points ?? [];
        var w = Bounds.Width;
        var vals = points.Where(p => p.Y.HasValue).Select(p => p.Y!.Value).ToList();
        if (vals.Count == 0 || w <= PadL + PadR)
        {
            ctx.DrawText(Text("no data yet", 12, C("InkSoft")), new Point(0, 4));
            return;
        }

        double min = vals.Min(), max = vals.Max();
        if (BandFrom is { } bf && BandTo is { } bt)
        {
            min = Math.Min(min, bf);
            max = Math.Max(max, bt);
        }
        var bands = Bands ?? [];
        foreach (var b in bands)
        {
            min = Math.Min(min, b.From);
            max = Math.Max(max, b.To);
        }
        var range = max - min;
        if (range == 0) range = 1;
        // Pad the axis only when there are no zone bands (they should fill the plot).
        if (bands.Count == 0)
        {
            min -= range * 0.15;
            max += range * 0.15;
        }
        if (max == min) max = min + 1;

        var n = points.Count;
        var iw = w - PadL - PadR;
        double X(int i) => PadL + (n <= 1 ? iw / 2 : i * iw / (n - 1));
        double Y(double v) => PadT + (1 - (v - min) / (max - min)) * (H - PadT - PadB);

        foreach (var b in bands)
            ctx.FillRectangle(new SolidColorBrush(ZoneColor(b.Color), 0.16), new Rect(PadL, Y(b.To), iw, Y(b.From) - Y(b.To)));
        if (BandFrom is { } from && BandTo is { } to)
            ctx.DrawRectangle(new SolidColorBrush(C(BandColorKey), 0.22), null,
                new RoundedRect(new Rect(PadL, Y(to), iw, Y(from) - Y(to)), 4));

        var soft = C("InkSoft");
        foreach (var v in new[] { min, (min + max) / 2, max })
        {
            var t = Text(Math.Round(v).ToString(CultureInfo.InvariantCulture), 10, soft);
            ctx.DrawText(t, new Point(PadL - 6 - t.Width, Y(v) - t.Height / 2));
        }

        var stroke = C(LineColorKey);
        var valid = points.Select((p, i) => (p, i)).Where(x => x.p.Y.HasValue).ToList();
        if (valid.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(valid[0].i), Y(valid[0].p.Y!.Value)), false);
                foreach (var (p, i) in valid.Skip(1)) g.LineTo(new Point(X(i), Y(p.Y!.Value)));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(stroke), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
        }

        var dotPen = new Pen(new SolidColorBrush(stroke), 2.5);
        var card = B("Card");
        var ink = C("InkStrong");
        foreach (var (p, i) in valid)
        {
            var at = new Point(X(i), Y(p.Y!.Value));
            ctx.DrawEllipse(card, dotPen, at, 4, 4);
            _dots.Add((at, p));

            // Value label above the dot (below it when the dot hugs the top), kept inside the plot.
            // Whole numbers from 100 up (Hz): a decimal there only crowds the neighbouring labels.
            var digits = Math.Abs(p.Y!.Value) >= 100 ? 0 : 1;
            var label = Text(Math.Round(p.Y!.Value, digits).ToString(CultureInfo.InvariantCulture), 10, ink, FontWeight.SemiBold);
            var lx = Math.Clamp(at.X - label.Width / 2, PadL, w - PadR - label.Width);
            var ly = at.Y - 8 - label.Height < 0 ? at.Y + 7 : at.Y - 7 - label.Height;
            ctx.DrawText(label, new Point(lx, ly));
        }

        for (var i = 0; i < n; i++)
        {
            var t = Text(points[i].Label, 10, soft);
            ctx.DrawText(t, new Point(X(i) - t.Width / 2, H - PadB + 8));
        }
    }
}
