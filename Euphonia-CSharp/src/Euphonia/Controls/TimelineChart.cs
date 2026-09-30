using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Euphonia.Core.Analysis;
using Euphonia.Core.Domain;

namespace Euphonia.Controls;

/// <summary>
/// A live metric over time, drawn on top of that metric's zone bands (the same
/// zones and colours as its stat card): the smoothed value as a bold line and the
/// current point as a dot at its end. NaN values break the line. The y scale is
/// fixed to the card's scale so the bands never move; the time axis shows
/// <see cref="AxisStart"/> … <see cref="AxisStart"/> + <see cref="AxisDuration"/>.
/// A new <see cref="Line"/> is not drawn at once: the drawn line glides from what was
/// on screen to the new values over <see cref="EaseSeconds"/> (display only), so values
/// that arrive in bursts or get corrected move smoothly instead of jumping.
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

    /// <summary>How long the drawn line takes to reach newly arrived values.</summary>
    public const double EaseSeconds = 0.25;
    /// <summary>A newly appended tail grows out of the old line's end when it starts within this (s).</summary>
    private const double ContinueSeconds = 0.5;

    private IReadOnlyList<TimedValue>? _easeFrom;
    private long _easeStart;
    private bool _frameRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LineProperty) return;
        var old = change.OldValue as IReadOnlyList<TimedValue>;
        // Start from what is on screen now (possibly mid-glide), not from the old target.
        _easeFrom = old is null ? null : Blend(_easeFrom, old, Progress());
        _easeStart = Stopwatch.GetTimestamp();
        RequestFrame();
    }

    private double Progress() =>
        _easeFrom is null ? 1 : Math.Clamp(Stopwatch.GetElapsedTime(_easeStart).TotalSeconds / EaseSeconds, 0, 1);

    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } top) return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
            if (Progress() < 1) RequestFrame();
            else _easeFrom = null;
        });
    }

    /// <summary>The line <paramref name="to"/>, with each value moved <paramref name="p"/> of the way from <paramref name="from"/>'s value at that time.</summary>
    private static IReadOnlyList<TimedValue> Blend(IReadOnlyList<TimedValue>? from, IReadOnlyList<TimedValue> to, double p)
    {
        if (from is not { Count: > 0 } || p >= 1) return to;
        var e = 1 - Math.Pow(1 - p, 3); // ease-out
        var result = new TimedValue[to.Count];
        for (var i = 0; i < to.Count; i++)
        {
            var v = to[i].Value;
            if (!double.IsNaN(v) && ValueAt(from, to[i].T) is { } old) v = old + (v - old) * e;
            result[i] = new TimedValue(to[i].T, v);
        }
        return result;
    }

    /// <summary>The drawn value at time t: interpolated inside a stretch, or the stretch's end value just after it.</summary>
    private static double? ValueAt(IReadOnlyList<TimedValue> line, double t)
    {
        int lo = 0, hi = line.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (line[mid].T < t) lo = mid + 1;
            else hi = mid;
        }
        // lo = first index with T >= t
        if (lo < line.Count && line[lo].T == t) return double.IsNaN(line[lo].Value) ? null : line[lo].Value;
        var left = lo - 1;
        if (left < 0 || double.IsNaN(line[left].Value)) return null;
        if (lo < line.Count && !double.IsNaN(line[lo].Value))
        {
            var a = line[left];
            var b = line[lo];
            return a.Value + (b.Value - a.Value) * (t - a.T) / (b.T - a.T);
        }
        // Past the end of a stretch: new data continuing it grows out of its last value.
        return lo == line.Count && t - line[left].T <= ContinueSeconds ? line[left].Value : null;
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

        var line = Line is { } target ? Blend(_easeFrom, target, Progress()) : null;
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
