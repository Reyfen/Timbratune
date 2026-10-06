using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// A live metric over time, drawn on top of that metric's zone bands (the same
/// zones and colours as its stat card): the smoothed value as a bold line and the
/// current point as a dot at its end. NaN values break the line. The y scale is
/// fixed to the card's scale so the bands never move; the time axis shows
/// <see cref="AxisStart"/> … <see cref="AxisStart"/> + <see cref="AxisDuration"/>.
/// A new <see cref="Line"/> is not drawn at once: it eases in (<see cref="LineEasing"/>).
/// </summary>
public sealed class TimelineChart : ThemedControl
{
    public static readonly StyledProperty<IReadOnlyList<TimedValue>?> LineProperty =
        AvaloniaProperty.Register<TimelineChart, IReadOnlyList<TimedValue>?>(nameof(Line));
    public static readonly StyledProperty<IReadOnlyList<Zone>?> ZonesProperty =
        AvaloniaProperty.Register<TimelineChart, IReadOnlyList<Zone>?>(nameof(Zones));
    public static readonly StyledProperty<double> LoProperty =
        AvaloniaProperty.Register<TimelineChart, double>(nameof(Lo));
    public static readonly StyledProperty<double> HiProperty =
        AvaloniaProperty.Register<TimelineChart, double>(nameof(Hi), 1);
    public static readonly StyledProperty<double> AxisDurationProperty =
        AvaloniaProperty.Register<TimelineChart, double>(nameof(AxisDuration), 10);
    public static readonly StyledProperty<double> AxisStartProperty =
        AvaloniaProperty.Register<TimelineChart, double>(nameof(AxisStart));

    static TimelineChart() =>
        RedrawOn<TimelineChart>(LineProperty, ZonesProperty, LoProperty, HiProperty, AxisDurationProperty, AxisStartProperty);

    public IReadOnlyList<TimedValue>? Line { get => GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }
    public double Lo { get => GetValue(LoProperty); set => SetValue(LoProperty, value); }
    public double Hi { get => GetValue(HiProperty); set => SetValue(HiProperty, value); }
    public double AxisDuration { get => GetValue(AxisDurationProperty); set => SetValue(AxisDurationProperty, value); }
    public double AxisStart { get => GetValue(AxisStartProperty); set => SetValue(AxisStartProperty, value); }

    private const double H = 130, PadL = 36, PadR = 12, PadT = 8, PadB = 20;

    private readonly LineEasing _easing;

    public TimelineChart() => _easing = new LineEasing(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty) _easing.Retarget(change.OldValue as IReadOnlyList<TimedValue>);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width, H);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (w <= PadL + PadR || Hi <= Lo) return;
        var iw = w - PadL - PadR;
        var ih = H - PadT - PadB;
        var duration = AxisDuration > 0 ? AxisDuration : 10;
        var start = AxisStart;
        double X(double t) => PadL + Math.Clamp((t - start) / duration, 0, 1) * iw;
        double Y(double v) => PadT + (1 - (Math.Clamp(v, Lo, Hi) - Lo) / (Hi - Lo)) * ih;

        // Zone bands across the whole plot; the outer zones extend to the edges.
        var zones = Zones ?? [];
        if (zones.Count == 0) ctx.FillRectangle(B("Card2"), new Rect(PadL, PadT, iw, ih));
        for (var z = 0; z < zones.Count; z++)
        {
            var top = z == zones.Count - 1 ? Hi : Math.Min(Hi, zones[z].To);
            var bottom = z == 0 ? Lo : Math.Max(Lo, zones[z].From);
            if (top <= bottom) continue;
            ctx.FillRectangle(new SolidColorBrush(ZoneColor(zones[z].Color), 0.35), new Rect(PadL, Y(top), iw, Y(bottom) - Y(top)));
        }

        var soft = C("InkSoft");
        foreach (var v in new[] { Lo, Hi }.Concat(zones.Skip(1).Select(z => z.From)).Where(v => v > Lo && v < Hi || v == Lo || v == Hi).Distinct())
        {
            var label = Text(Math.Round(v, 1).ToString(CultureInfo.InvariantCulture), 10, soft);
            ctx.DrawText(label, new Point(PadL - 5 - label.Width, Y(v) - label.Height / 2));
        }
        ctx.DrawText(Text(Seconds(start), 10, soft), new Point(PadL, H - PadB + 4));
        var end = Text(Seconds(start + duration), 10, soft);
        ctx.DrawText(end, new Point(w - PadR - end.Width, H - PadB + 4));

        var line = Line is { } target ? _easing.Current(target) : null;
        DrawLine(ctx, line, new Pen(B("InkStrong"), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), X, Y, start);
        // The current point: the latest settled-enough value (it stays put through a pause).
        var lastIndex = line is null ? -1 : FindLastValue(line);
        if (lastIndex >= 0 && line![lastIndex].T >= start)
        {
            var last = line[lastIndex];
            ctx.DrawEllipse(B("Card"), new Pen(B("InkStrong"), 2.5), new Point(X(last.T), Y(last.Value)), 5.5, 5.5);
        }
    }

    private static int FindLastValue(IReadOnlyList<TimedValue> line)
    {
        for (var i = line.Count - 1; i >= 0; i--)
            if (!double.IsNaN(line[i].Value)) return i;
        return -1;
    }

    internal static string Seconds(double t) => $"{Math.Round(t).ToString("0", CultureInfo.InvariantCulture)} s";

    private static void DrawLine(DrawingContext ctx, IReadOnlyList<TimedValue>? points, Pen pen, Func<double, double> x,
        Func<double, double> y, double start)
    {
        if (points is not { Count: > 0 }) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var open = false;
            foreach (var p in points)
            {
                if (double.IsNaN(p.Value) || p.T < start)
                {
                    if (open) g.EndFigure(false);
                    open = false;
                    continue;
                }
                var at = new Point(x(p.T), y(p.Value));
                if (!open)
                {
                    g.BeginFigure(at, false);
                    g.LineTo(at); // a lone point still shows as a dot with round caps
                    open = true;
                }
                else g.LineTo(at);
            }
            if (open) g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
