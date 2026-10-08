using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Controls;

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
    /// <summary>
    /// Live view: the smoothed pitch line (NaN = unvoiced) drawn in place of the raw
    /// frames, eased in like the other live lines (<see cref="LineEasing"/>).
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<TimedValue>?> LiveLineProperty =
        AvaloniaProperty.Register<ContourChart, IReadOnlyList<TimedValue>?>(nameof(LiveLine));
    /// <summary>When set (live view), these zones fill the background and the pitch range is fixed.</summary>
    public static readonly StyledProperty<IReadOnlyList<Zone>?> ZonesProperty =
        AvaloniaProperty.Register<ContourChart, IReadOnlyList<Zone>?>(nameof(Zones));

    static ContourChart() => RedrawOn<ContourChart>(DetailProperty, FemThresholdProperty, AxisDurationProperty, AxisStartProperty, ZonesProperty, LiveLineProperty);

    public RecordingDetail? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public double FemThreshold { get => GetValue(FemThresholdProperty); set => SetValue(FemThresholdProperty, value); }
    public double AxisDuration { get => GetValue(AxisDurationProperty); set => SetValue(AxisDurationProperty, value); }
    public double AxisStart { get => GetValue(AxisStartProperty); set => SetValue(AxisStartProperty, value); }
    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }
    public IReadOnlyList<TimedValue>? LiveLine { get => GetValue(LiveLineProperty); set => SetValue(LiveLineProperty, value); }

    private readonly LineEasing _easing;

    public ContourChart() => _easing = new LineEasing(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LiveLineProperty) _easing.Retarget(change.OldValue as IReadOnlyList<TimedValue>);
    }

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
        if (!IsOnScreen) return;
        using var perf = Diagnostics.Perf.Measure("render.ContourChart");
        _dots.Clear();
        var d = Detail;
        var w = Bounds.Width;
        if (d is null || w <= PadL + PadR) return;

        var ih = H - PadT - PadB;
        var iw = w - PadL - PadR;
        var floor = d.RegisterFloorHz;
        // The contour to draw: the live smoothed line when given, otherwise the 10 ms frames.
        // Read in place (no copies: this runs every frame while a live line glides); NaN = unvoiced.
        var eased = LiveLine is { } live ? _easing.Current(live) : null;
        var frames = d.Frames;
        var count = eased?.Count ?? Math.Min(frames.T.Count, frames.Hz.Count);
        double TimeAt(int i) => eased is not null ? eased[i].T : frames.T[i];
        double HzAt(int i) => eased is not null ? eased[i].Value : frames.Hz[i] ?? double.NaN;
        var dur = AxisDuration > 0 ? AxisDuration : d.DurationS > 0 ? d.DurationS : count > 0 && TimeAt(count - 1) > 0 ? TimeAt(count - 1) : 1;
        var fem = FemThreshold;
        var zones = Zones;

        double maxHz, minHz;
        if (zones is null)
        {
            double top = 0, bottom = double.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var f = HzAt(i);
                if (double.IsNaN(f)) continue;
                top = Math.Max(top, f);
                bottom = Math.Min(bottom, f);
            }
            maxHz = Math.Max(Math.Max(220, top), fem) * 1.05;
            minHz = Math.Min(Math.Min(80, floor - 20), bottom);
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
        // The floor's label goes on top of the contour, on a card-coloured plate, so lines crossing it don't hide it.
        var floorLabel = Text($"register floor {floor.ToString(CultureInfo.InvariantCulture)} Hz", 11, C("ZoneMascInk"));
        var floorLabelAt = new Point(w - PadR - floorLabel.Width - 3, Y(floor) - 5 - floorLabel.Height);
        void DrawFloorLabel()
        {
            ctx.FillRectangle(new SolidColorBrush(C("Card"), 0.85),
                new Rect(floorLabelAt.X - 4, floorLabelAt.Y - 1, floorLabel.Width + 8, floorLabel.Height + 2), 4);
            ctx.DrawText(floorLabel, floorLabelAt);
        }

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

        // Contour runs, split on unvoiced gaps and coloured by side of the floor (runs meet where
        // the line crosses it). Over the zone bands (live) the upper runs use a darker ink so
        // they stay visible on the pink band. The saved take's raw frames are joined across
        // short unvoiced gaps inside a phrase, as the live line is (it arrives already joined).
        var isLive = LiveLine is not null;
        var bridge = ViewModels.LiveTimelinesViewModel.ContourBridgeGap;
        var belowPen = CachedPen(ref _belowPen, C("ZoneMascInk"), 1, 3);
        var abovePen = zones is null
            ? CachedPen(ref _abovePen, ZoneColor(ZoneColorKey.Fem), 0.9, 2.4)
            : CachedPen(ref _abovePen, C("AccentEmphasis"), 1, 2.4);
        var run = _run;
        run.Clear();
        bool? runBelow = null;
        // At most about one point per pixel: a point closer than MinStep px to the last one drawn
        // is skipped, except a run's last point (kept as `pending` until the run ends). The live
        // line has ~50 points a second, i.e. a few per pixel on a phone; drawing them all only cost time.
        const double MinStep = 0.75;
        Point? pending = null;
        void Flush()
        {
            if (pending is { } last) run.Add(last);
            pending = null;
            if (run.Count > 0 && runBelow is { } below) DrawRun(ctx, run, below ? belowPen : abovePen);
            run.Clear();
        }
        double? lastT = null; // the previous voiced frame, while a run can still continue from it
        for (var i = 0; i < count; i++)
        {
            var f = HzAt(i);
            var ti = TimeAt(i);
            if (double.IsNaN(f) || ti < start)
            {
                // The live line marks its own breaks; raw frames are judged by the gap's length below.
                if (isLive)
                {
                    Flush();
                    runBelow = null;
                    lastT = null;
                }
                continue;
            }
            if (!isLive && lastT is { } prev && (ti - prev > bridge + 0.011 || EndsPhraseBetween(d.Phrases, prev, ti)))
            {
                Flush();
                runBelow = null;
            }
            var at = new Point(X(ti), Y(f));
            var isBelow = f < floor;
            if (runBelow != isBelow)
            {
                var joint = pending ?? (run.Count > 0 ? run[^1] : (Point?)null);
                Flush();
                if (joint is { } j) run.Add(j);
                runBelow = isBelow;
            }
            if (run.Count == 0 || at.X - run[^1].X >= MinStep)
            {
                run.Add(at);
                pending = null;
            }
            else pending = at;
            lastT = ti;
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
            for (var i = count - 1; i >= 0; i--)
            {
                var f = HzAt(i);
                if (double.IsNaN(f)) continue;
                var at = new Point(X(TimeAt(i)), Y(f));
                ctx.DrawEllipse(B("Card"), new Pen(B("InkStrong"), 2.5), at, 6, 6);
                break;
            }
            DrawFloorLabel();
            var end = Text(TimelineChart.Seconds(start + dur), 11, soft);
            ctx.DrawText(Text(TimelineChart.Seconds(start), 11, soft), new Point(PadL, H - 18));
            ctx.DrawText(end, new Point(w - PadR - end.Width, H - 18));
            return;
        }

        DrawFloorLabel();
        ctx.DrawText(Text("time →", 11, soft), new Point(PadL, H - 18));
        var legend = Text("● dots = how each phrase landed", 11, soft);
        ctx.DrawText(legend, new Point(w - PadR - legend.Width, H - 18));
    }

    private readonly List<Point> _run = [];
    private Pen? _belowPen, _abovePen;

    /// <summary>A pen kept between frames (rebuilt only when its colour changes, e.g. with the theme).</summary>
    private static Pen CachedPen(ref Pen? pen, Color color, double opacity, double thickness)
    {
        if (pen?.Brush is SolidColorBrush b && b.Color == color && b.Opacity == opacity && pen.Thickness == thickness) return pen;
        return pen = new Pen(new SolidColorBrush(color, opacity), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    }

    private static bool EndsPhraseBetween(List<Core.Models.Phrase> phrases, double from, double to)
    {
        foreach (var p in phrases)
            if (p.End > from && p.End < to) return true;
        return false;
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
