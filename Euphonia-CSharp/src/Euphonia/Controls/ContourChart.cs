using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Euphonia.Core.Domain;
using Euphonia.Core.Models;

namespace Euphonia.Controls;

/// <summary>
/// Port of ContourChart.tsx: the 10 ms pitch contour over time with the
/// register floor across it. Stretches below the floor are painted in the
/// masculine-zone ink so register breaks jump out; a dot under each phrase
/// ending shows whether it landed (pink) or fell out (blue).
/// </summary>
public sealed class ContourChart : ThemedControl
{
    public static readonly StyledProperty<RecordingDetail?> DetailProperty =
        AvaloniaProperty.Register<ContourChart, RecordingDetail?>(nameof(Detail));
    public static readonly StyledProperty<double> FemThresholdProperty =
        AvaloniaProperty.Register<ContourChart, double>(nameof(FemThreshold), Metrics.FemininePitchHz);

    /// <summary>Time axis length (s); 0 = the take's duration. Live views pass 10-second steps.</summary>
    public static readonly StyledProperty<double> AxisDurationProperty =
        AvaloniaProperty.Register<ContourChart, double>(nameof(AxisDuration));
    /// <summary>Time at the left edge (s); live views with a sliding window move it along.</summary>
    public static readonly StyledProperty<double> AxisStartProperty =
        AvaloniaProperty.Register<ContourChart, double>(nameof(AxisStart));
    /// <summary>When set (live view), these zones fill the background and the pitch range is fixed.</summary>
    public static readonly StyledProperty<IReadOnlyList<Zone>?> ZonesProperty =
        AvaloniaProperty.Register<ContourChart, IReadOnlyList<Zone>?>(nameof(Zones));

    static ContourChart() => RedrawOn<ContourChart>(DetailProperty, FemThresholdProperty, AxisDurationProperty, AxisStartProperty, ZonesProperty);

    public RecordingDetail? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public double FemThreshold { get => GetValue(FemThresholdProperty); set => SetValue(FemThresholdProperty, value); }
    public double AxisDuration { get => GetValue(AxisDurationProperty); set => SetValue(AxisDurationProperty, value); }
    public double AxisStart { get => GetValue(AxisStartProperty); set => SetValue(AxisStartProperty, value); }
    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }

    // Fixed live pitch range: wide enough for low male and high female speech, so zone bands never move.
    private const double LiveMinHz = 70, LiveMaxHz = 330;

    private const double H = 240;
    private const double PadL = 40, PadR = 14, PadT = 14, PadB = 40;
    private readonly List<(Point At, string Tip)> _dots = [];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 900 : Math.Max(260, availableSize.Width), H);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var hit = _dots.FirstOrDefault(d => Math.Abs(d.At.X - pos.X) < 8 && Math.Abs(d.At.Y - pos.Y) < 8);
        ToolTip.SetTip(this, hit.Tip);
    }

    public override void Render(DrawingContext ctx)
    {
        _dots.Clear();
        var d = Detail;
        var w = Bounds.Width;
        if (d is null || w <= PadL + PadR) return;

        var ih = H - PadT - PadB;
        var iw = w - PadL - PadR;
        var floor = d.RegisterFloorHz;
        var t = d.Frames.T;
        var hz = d.Frames.Hz;
        var dur = AxisDuration > 0 ? AxisDuration : d.DurationS > 0 ? d.DurationS : t.Count > 0 && t[^1] > 0 ? t[^1] : 1;
        var fem = FemThreshold;
        var zones = Zones;

        var voiced = hz.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        double maxHz, minHz;
        if (zones is null)
        {
            maxHz = Math.Max(Math.Max(220, voiced.Count > 0 ? voiced.Max() : 0), fem) * 1.05;
            minHz = Math.Min(Math.Min(80, floor - 20), voiced.Count > 0 ? voiced.Min() : double.MaxValue);
        }
        else
        {
            maxHz = LiveMaxHz;
            minHz = LiveMinHz;
        }

        var start = AxisStart;
        double X(double time) => PadL + (time - start) / dur * iw;
        double Y(double f) => PadT + (1 - (Math.Clamp(f, minHz, maxHz) - minHz) / (maxHz - minHz)) * ih;

        if (zones is null)
        {
            ctx.FillRectangle(new SolidColorBrush(ZoneColor(ZoneColorKey.Fem), 0.12), new Rect(PadL, Y(maxHz), iw, Y(fem) - Y(maxHz)));
            ctx.FillRectangle(new SolidColorBrush(ZoneColor(ZoneColorKey.Masc), 0.22), new Rect(PadL, Y(floor), iw, Y(minHz) - Y(floor)));
        }
        else
        {
            // Zone bands; the first and last extend to the plot edges (values beyond read as that zone).
            for (var z = 0; z < zones.Count; z++)
            {
                var top = z == zones.Count - 1 ? maxHz : zones[z].To;
                var bottom = z == 0 ? minHz : zones[z].From;
                ctx.FillRectangle(new SolidColorBrush(ZoneColor(zones[z].Color), 0.3), new Rect(PadL, Y(top), iw, Y(bottom) - Y(top)));
            }
        }

        var floorPen = new Pen(new SolidColorBrush(Color.Parse("#7c9fd6")), 1.5) { DashStyle = new DashStyle([5 / 1.5, 4 / 1.5], 0) };
        ctx.DrawLine(floorPen, new Point(PadL, Y(floor)), new Point(w - PadR, Y(floor)));
        var floorLabel = Text($"register floor {floor.ToString(CultureInfo.InvariantCulture)} Hz", 11, C("ZoneMascInk"));
        ctx.DrawText(floorLabel, new Point(w - PadR - floorLabel.Width, Y(floor) - 5 - floorLabel.Height));

        var soft = C("InkSoft");
        var ticks = zones is null
            ? new[] { minHz, floor, fem, maxHz }
            : new[] { minHz, maxHz }.Concat(zones.Skip(1).Select(z => z.From)).Append(floor).ToArray();
        foreach (var v in ticks.Distinct().Where(v => v <= maxHz && v >= minHz))
        {
            var label = Text(Math.Round(v).ToString(CultureInfo.InvariantCulture), 10, soft);
            ctx.DrawText(label, new Point(PadL - 6 - label.Width, Y(v) - label.Height / 2));
        }

        // Everything drawn against time stays inside the plot's x range (a sliding window scrolls it).
        using var clip = ctx.PushClip(new Rect(PadL - 7, 0, iw + 14, H));
        var divider = new Pen(B("LineSoft"), 1);
        foreach (var p in d.Phrases.Where(p => p.End >= start)) ctx.DrawLine(divider, new Point(X(p.End), PadT), new Point(X(p.End), PadT + ih));

        // Contour runs, split on unvoiced gaps and on crossing the floor. Over the zone bands
        // (live) the upper runs use a darker ink so they stay visible on the pink band.
        var belowPen = new Pen(B("ZoneMascInk"), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var abovePen = zones is null
            ? new Pen(new SolidColorBrush(ZoneColor(ZoneColorKey.Fem), 0.9), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            : new Pen(B("AccentEmphasis"), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var run = new List<Point>();
        bool? runBelow = null;
        void Flush()
        {
            if (run.Count > 0 && runBelow is { } below) DrawRun(ctx, run, below ? belowPen : abovePen);
            run.Clear();
        }
        for (var i = 0; i < hz.Count && i < t.Count; i++)
        {
            if (hz[i] is not { } f || t[i] < start)
            {
                Flush();
                runBelow = null;
                continue;
            }
            var isBelow = f < floor;
            if (runBelow != isBelow)
            {
                Flush();
                runBelow = isBelow;
            }
            run.Add(new Point(X(t[i]), Y(f)));
        }
        Flush();

        var card = new Pen(B("Card"), 1.5);
        for (var k = 0; k < d.Phrases.Count; k++)
        {
            var p = d.Phrases[k];
            if (p.End < start) continue;
            var at = new Point(X(p.End), PadT + ih + 14);
            ctx.DrawEllipse(new SolidColorBrush(ZoneColor(p.EndedInRegister ? ZoneColorKey.Fem : ZoneColorKey.Masc)), card, at, 4.5, 4.5);
            _dots.Add((at, $"phrase {k + 1}: ended {p.OffsetHz.ToString(CultureInfo.InvariantCulture)} Hz — " +
                           (p.EndedInRegister ? "landed in register 💕" : "fell out of register")));
        }

        if (zones is not null)
        {
            // The current point: the latest voiced frame.
            for (var i = Math.Min(hz.Count, t.Count) - 1; i >= 0; i--)
            {
                if (hz[i] is not { } f) continue;
                var at = new Point(X(t[i]), Y(f));
                ctx.DrawEllipse(B("Card"), new Pen(B("InkStrong"), 2.5), at, 6, 6);
                break;
            }
            var end = Text(TimelineChart.Seconds(start + dur), 11, soft);
            ctx.DrawText(Text(TimelineChart.Seconds(start), 11, soft), new Point(PadL, H - 18));
            ctx.DrawText(end, new Point(w - PadR - end.Width, H - 18));
            return;
        }

        ctx.DrawText(Text("time →", 11, soft), new Point(PadL, H - 18));
        var legend = Text("● dots = how each phrase landed", 11, soft);
        ctx.DrawText(legend, new Point(w - PadR - legend.Width, H - 18));
    }

    private static void DrawRun(DrawingContext ctx, List<Point> pts, Pen pen)
    {
        if (pts.Count == 1)
        {
            ctx.DrawLine(pen, pts[0], pts[0]); // round cap renders a dot
            return;
        }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], false);
            for (var i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
