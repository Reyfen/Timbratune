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
/// A new <see cref="Line"/> is not drawn at once: it eases in (<see cref="LineEasing"/>). The zone
/// bands and labels are a layer of their own, so the glide only redraws the line.
/// </summary>
public sealed class TimelineChart : LayeredChart
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

    public IReadOnlyList<TimedValue>? Line { get => GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }
    public double Lo { get => GetValue(LoProperty); set => SetValue(LoProperty, value); }
    public double Hi { get => GetValue(HiProperty); set => SetValue(HiProperty, value); }
    public double AxisDuration { get => GetValue(AxisDurationProperty); set => SetValue(AxisDurationProperty, value); }
    public double AxisStart { get => GetValue(AxisStartProperty); set => SetValue(AxisStartProperty, value); }

    private const double H = 130, PadL = 36, PadR = 12, PadT = 8, PadB = 20;

    private readonly LineEasing _easing;
    private readonly ChartLayer _back, _line;

    public TimelineChart()
    {
        _back = AddLayer(RenderBack);
        _line = AddLayer(RenderLine);
        _easing = new LineEasing(this, _line.InvalidateVisual);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty) _easing.Retarget(change.OldValue as IReadOnlyList<TimedValue>);
        else if (change.Property == ZonesProperty || change.Property == LoProperty || change.Property == HiProperty
                 || change.Property == AxisDurationProperty || change.Property == AxisStartProperty) _back.InvalidateVisual();
        else return;
        _line.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width, H);

    private readonly record struct Layout(double W, double Iw, double Ih, double Start, double Duration, double Lo, double Hi)
    {
        public double X(double t) => PadL + Math.Clamp((t - Start) / Duration, 0, 1) * Iw;
        public double Y(double v) => PadT + (1 - (Math.Clamp(v, Lo, Hi) - Lo) / (Hi - Lo)) * Ih;
    }

    private Layout? Plot()
    {
        var w = Bounds.Width;
        if (!IsOnScreen || w <= PadL + PadR || Hi <= Lo) return null;
        return new Layout(w, w - PadL - PadR, H - PadT - PadB, AxisStart, AxisDuration > 0 ? AxisDuration : 10, Lo, Hi);
    }

    // The zone bands and the axis labels: redrawn only when the scale, zones or time axis change.
    private void RenderBack(DrawingContext ctx)
    {
        if (Plot() is not { } p) return;
        using var perf = Diagnostics.Perf.Measure("render.TimelineChart.back");
        // Zone bands across the whole plot; the outer zones extend to the edges.
        var zones = Zones ?? [];
        if (zones.Count == 0) ctx.FillRectangle(B("Card2"), new Rect(PadL, PadT, p.Iw, p.Ih));
        for (var z = 0; z < zones.Count; z++)
        {
            var top = z == zones.Count - 1 ? p.Hi : Math.Min(p.Hi, zones[z].To);
            var bottom = z == 0 ? p.Lo : Math.Max(p.Lo, zones[z].From);
            if (top <= bottom) continue;
            ctx.FillRectangle(ZoneBrush(z, ZoneColor(zones[z].Color)), new Rect(PadL, p.Y(top), p.Iw, p.Y(bottom) - p.Y(top)));
        }

        var soft = C("InkSoft");
        // Axis values: the scale's ends and the inner zone boundaries.
        void AxisLabel(double v)
        {
            var label = Text(Math.Round(v, 1).ToString(CultureInfo.InvariantCulture), 10, soft);
            ctx.DrawText(label, new Point(PadL - 5 - label.Width, p.Y(v) - label.Height / 2));
        }
        AxisLabel(p.Lo);
        AxisLabel(p.Hi);
        for (var k = 1; k < zones.Count; k++)
            if (zones[k].From > p.Lo && zones[k].From < p.Hi) AxisLabel(zones[k].From);
        ctx.DrawText(Text(Seconds(p.Start), 10, soft), new Point(PadL, H - PadB + 4));
        var end = Text(Seconds(p.Start + p.Duration), 10, soft);
        ctx.DrawText(end, new Point(p.W - PadR - end.Width, H - PadB + 4));
    }

    // The line and its current point: redrawn on every frame of a glide.
    private void RenderLine(DrawingContext ctx)
    {
        if (Plot() is not { } p) return;
        using var perf = Diagnostics.Perf.Measure("render.TimelineChart");
        var line = Line is { } target ? _easing.Current(target) : null;
        var ink = C("InkStrong");
        if (_linePen?.Brush is not SolidColorBrush lb || lb.Color != ink)
        {
            _linePen = new Pen(new SolidColorBrush(ink), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            _dotPen = new Pen(new SolidColorBrush(ink), 2.5);
        }
        var card = C("Card");
        if (_dotFill?.Color != card) _dotFill = new SolidColorBrush(card);
        DrawLine(ctx, line, _linePen, p);
        // The current point: the latest settled-enough value (it stays put through a pause).
        var lastIndex = line is null ? -1 : FindLastValue(line);
        if (lastIndex >= 0 && line![lastIndex].T >= p.Start)
        {
            var last = line[lastIndex];
            ctx.DrawEllipse(_dotFill, _dotPen, new Point(p.X(last.T), p.Y(last.Value)), 5.5, 5.5);
        }
    }

    private SolidColorBrush? _dotFill;
    private Pen? _linePen, _dotPen;
    private readonly List<SolidColorBrush> _zoneBrushes = [];

    /// <summary>Zone band brushes kept between frames (rebuilt when a colour changes).</summary>
    private SolidColorBrush ZoneBrush(int index, Color color)
    {
        while (_zoneBrushes.Count <= index) _zoneBrushes.Add(new SolidColorBrush(color, 0.35));
        if (_zoneBrushes[index].Color != color) _zoneBrushes[index] = new SolidColorBrush(color, 0.35);
        return _zoneBrushes[index];
    }

    private static int FindLastValue(IReadOnlyList<TimedValue> line)
    {
        for (var i = line.Count - 1; i >= 0; i--)
            if (!double.IsNaN(line[i].Value)) return i;
        return -1;
    }

    internal static string Seconds(double t) => $"{Math.Round(t).ToString("0", CultureInfo.InvariantCulture)} s";

    private static void DrawLine(DrawingContext ctx, IReadOnlyList<TimedValue>? points, Pen pen, Layout plot)
    {
        if (points is not { Count: > 0 }) return;
        // At most about one point per pixel (closer points are skipped, but each stretch keeps its
        // last point): the lines are redrawn every frame while they glide.
        const double MinStep = 0.75;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var open = false;
            var lastX = double.NegativeInfinity;
            Point? pending = null;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (double.IsNaN(p.Value) || p.T < plot.Start)
                {
                    if (open)
                    {
                        if (pending is { } last) g.LineTo(last);
                        g.EndFigure(false);
                    }
                    open = false;
                    pending = null;
                    continue;
                }
                var at = new Point(plot.X(p.T), plot.Y(p.Value));
                if (!open)
                {
                    g.BeginFigure(at, false);
                    g.LineTo(at); // a lone point still shows as a dot with round caps
                    open = true;
                    lastX = at.X;
                }
                else if (at.X - lastX >= MinStep)
                {
                    g.LineTo(at);
                    lastX = at.X;
                    pending = null;
                }
                else pending = at;
            }
            if (open)
            {
                if (pending is { } last) g.LineTo(last);
                g.EndFigure(false);
            }
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
