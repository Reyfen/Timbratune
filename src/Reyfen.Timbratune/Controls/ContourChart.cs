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
/// ending shows whether it landed (pink) or fell out (blue). It is drawn in
/// three layers (the bands and axis, the contour and dots, the labels on top),
/// so a gliding live line only redraws the contour.
/// </summary>
public sealed class ContourChart : LayeredChart
{
    /// <summary>Longest unvoiced gap inside a phrase that the pitch contours (live and saved) join with a straight line.</summary>
    public const double ContourBridgeGap = Core.Analysis.Metrics.Pitch.PitchModule.ContourBridgeGap;

    public static readonly StyledProperty<RecordingDetail?> DetailProperty =
        AvaloniaProperty.Register<ContourChart, RecordingDetail?>(nameof(Detail));
    public static readonly StyledProperty<double> FemThresholdProperty =
        AvaloniaProperty.Register<ContourChart, double>(nameof(FemThreshold), Core.Analysis.Metrics.Pitch.PitchModule.FemininePitchHz);

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

    public RecordingDetail? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public double FemThreshold { get => GetValue(FemThresholdProperty); set => SetValue(FemThresholdProperty, value); }
    public double AxisDuration { get => GetValue(AxisDurationProperty); set => SetValue(AxisDurationProperty, value); }
    public double AxisStart { get => GetValue(AxisStartProperty); set => SetValue(AxisStartProperty, value); }
    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }
    public IReadOnlyList<TimedValue>? LiveLine { get => GetValue(LiveLineProperty); set => SetValue(LiveLineProperty, value); }

    private readonly LineEasing _easing;
    private readonly ChartLayer _line;

    public ContourChart()
    {
        AddLayer(RenderBack);
        _line = AddLayer(RenderLine);
        AddLayer(RenderTop);
        _easing = new LineEasing(this, _line.InvalidateVisual);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LiveLineProperty)
        {
            _easing.Retarget(change.OldValue as IReadOnlyList<TimedValue>);
            _line.InvalidateVisual();
        }
        else if (change.Property == DetailProperty || change.Property == FemThresholdProperty || change.Property == AxisDurationProperty
                 || change.Property == AxisStartProperty || change.Property == ZonesProperty) Redraw();
    }

    // Fixed live pitch range: wide enough for low male and high female speech, so zone bands never move.
    private const double LiveMinHz = 70, LiveMaxHz = 330;

    private const double H = 240;
    private const double PadL = 40, PadR = 14, PadT = 14, PadB = 40;
    private readonly List<(Point At, int Phrase)> _dots = [];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 900 : Math.Max(260, availableSize.Width), H);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var hit = _dots.FindIndex(d => Math.Abs(d.At.X - pos.X) < 8 && Math.Abs(d.At.Y - pos.Y) < 8);
        string? tip = null;
        if (hit >= 0 && Detail?.Phrases is { } phrases && _dots[hit].Phrase < phrases.Count)
        {
            var k = _dots[hit].Phrase;
            var p = phrases[k];
            tip = $"phrase {k + 1}: ended {p.OffsetHz.ToString(CultureInfo.InvariantCulture)} Hz — " +
                  (p.EndedInRegister ? "landed in register 💕" : "fell out of register");
        }
        ToolTip.SetTip(this, tip);
    }

    /// <summary>Where things go, shared by the layers (refilled by each layer's redraw).</summary>
    private sealed class Layout
    {
        public RecordingDetail D = null!;
        public IReadOnlyList<Zone>? Zones;
        public double W, Iw, Ih, Start, Dur, MinHz, MaxHz, Floor, Fem;
        public double X(double time) => PadL + (time - Start) / Dur * Iw;
        public double Y(double f) => PadT + (1 - (Math.Clamp(f, MinHz, MaxHz) - MinHz) / (MaxHz - MinHz)) * Ih;
    }

    private readonly Layout _layout = new();

    // The contour to draw: the live smoothed line when given (eased), otherwise the 10 ms frames.
    // Read in place (no copies: the contour layer redraws every frame while a live line glides); NaN = unvoiced.
    private IReadOnlyList<TimedValue>? Eased() => LiveLine is { } live ? _easing.Current(live) : null;
    private static int CountOf(RecordingDetail d, IReadOnlyList<TimedValue>? eased) => eased?.Count ?? Math.Min(d.Frames.T.Count, d.Frames.Hz.Count);
    private static double TimeAt(RecordingDetail d, IReadOnlyList<TimedValue>? eased, int i) => eased is not null ? eased[i].T : d.Frames.T[i];
    private static double HzAt(RecordingDetail d, IReadOnlyList<TimedValue>? eased, int i) => eased is not null ? eased[i].Value : d.Frames.Hz[i] ?? double.NaN;

    private Layout? Plot(IReadOnlyList<TimedValue>? eased)
    {
        var d = Detail;
        var w = Bounds.Width;
        if (!IsOnScreen || d is null || w <= PadL + PadR) return null;
        var l = _layout;
        l.D = d;
        l.W = w;
        l.Ih = H - PadT - PadB;
        l.Iw = w - PadL - PadR;
        l.Floor = d.RegisterFloorHz;
        l.Fem = FemThreshold;
        l.Zones = Zones;
        l.Start = AxisStart;
        var count = CountOf(d, eased);
        l.Dur = AxisDuration > 0 ? AxisDuration : d.DurationS > 0 ? d.DurationS : count > 0 && TimeAt(d, eased, count - 1) > 0 ? TimeAt(d, eased, count - 1) : 1;
        if (l.Zones is null)
        {
            // The saved take (no glide): the range fits its frames.
            double top = 0, bottom = double.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var f = HzAt(d, eased, i);
                if (double.IsNaN(f)) continue;
                top = Math.Max(top, f);
                bottom = Math.Min(bottom, f);
            }
            l.MaxHz = Math.Max(Math.Max(220, top), l.Fem) * 1.05;
            l.MinHz = Math.Min(Math.Min(80, l.Floor - 20), bottom);
        }
        else
        {
            l.MaxHz = LiveMaxHz;
            l.MinHz = LiveMinHz;
        }
        return l;
    }

    // The bands, the register floor and the pitch axis: redrawn when the take, zones or axis change.
    private void RenderBack(DrawingContext ctx)
    {
        if (Plot(Eased()) is not { } l) return;
        using var perf = Diagnostics.Perf.Measure("render.ContourChart.back");
        var zones = l.Zones;
        if (zones is null)
        {
            ctx.FillRectangle(new SolidColorBrush(ZoneColor(ZoneColorKey.Fem), 0.12), new Rect(PadL, l.Y(l.MaxHz), l.Iw, l.Y(l.Fem) - l.Y(l.MaxHz)));
            ctx.FillRectangle(new SolidColorBrush(ZoneColor(ZoneColorKey.Masc), 0.22), new Rect(PadL, l.Y(l.Floor), l.Iw, l.Y(l.MinHz) - l.Y(l.Floor)));
        }
        else
        {
            // Zone bands; the first and last extend to the plot edges (values beyond read as that zone).
            for (var z = 0; z < zones.Count; z++)
            {
                var top = z == zones.Count - 1 ? l.MaxHz : zones[z].To;
                var bottom = z == 0 ? l.MinHz : zones[z].From;
                ctx.FillRectangle(new SolidColorBrush(ZoneColor(zones[z].Color), 0.3), new Rect(PadL, l.Y(top), l.Iw, l.Y(bottom) - l.Y(top)));
            }
        }

        var floorPen = new Pen(new SolidColorBrush(Color.Parse("#7c9fd6")), 1.5) { DashStyle = new DashStyle([5 / 1.5, 4 / 1.5], 0) };
        ctx.DrawLine(floorPen, new Point(PadL, l.Y(l.Floor)), new Point(l.W - PadR, l.Y(l.Floor)));

        var soft = C("InkSoft");
        var ticks = zones is null
            ? new[] { l.MinHz, l.Floor, l.Fem, l.MaxHz }
            : new[] { l.MinHz, l.MaxHz }.Concat(zones.Skip(1).Select(z => z.From)).Append(l.Floor).ToArray();
        foreach (var v in ticks.Distinct().Where(v => v <= l.MaxHz && v >= l.MinHz))
        {
            var label = Text(Math.Round(v).ToString(CultureInfo.InvariantCulture), 10, soft);
            ctx.DrawText(label, new Point(PadL - 6 - label.Width, l.Y(v) - label.Height / 2));
        }
    }

    // The phrase dividers, the contour, the phrase dots and the current point: redrawn on every
    // frame of a glide, so nothing here allocates but the line's geometry.
    private void RenderLine(DrawingContext ctx)
    {
        var eased = Eased();
        _dots.Clear();
        if (Plot(eased) is not { } l) return;
        using var perf = Diagnostics.Perf.Measure("render.ContourChart");
        var d = l.D;
        var start = l.Start;
        var floor = l.Floor;
        var count = CountOf(d, eased);
        RefreshInks();

        // Everything drawn against time stays inside the plot's x range (a sliding window scrolls it).
        using var clip = ctx.PushClip(new Rect(PadL - 7, 0, l.Iw + 14, H));
        foreach (var p in d.Phrases)
            if (p.End >= start) ctx.DrawLine(_divider!, new Point(l.X(p.End), PadT), new Point(l.X(p.End), PadT + l.Ih));

        // Contour runs, split on unvoiced gaps and coloured by side of the floor (runs meet where
        // the line crosses it). Over the zone bands (live) the upper runs use a darker ink so
        // they stay visible on the pink band. The saved take's raw frames are joined across
        // short unvoiced gaps inside a phrase, as the live line is (it arrives already joined).
        var isLive = eased is not null;
        var bridge = ContourBridgeGap;
        var belowPen = CachedPen(ref _belowPen, C("ZoneMascInk"), 1, 3);
        var abovePen = l.Zones is null
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
            var f = HzAt(d, eased, i);
            var ti = TimeAt(d, eased, i);
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
            var at = new Point(l.X(ti), l.Y(f));
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

        // A dot under each phrase ending; its tooltip is only written when hovered.
        for (var k = 0; k < d.Phrases.Count; k++)
        {
            var p = d.Phrases[k];
            if (p.End < start) continue;
            var at = new Point(l.X(p.End), PadT + l.Ih + 14);
            ctx.DrawEllipse(p.EndedInRegister ? _femDot : _mascDot, _cardRim, at, 4.5, 4.5);
            _dots.Add((at, k));
        }

        if (l.Zones is null) return;
        // The current point: the latest voiced frame.
        for (var i = count - 1; i >= 0; i--)
        {
            var f = HzAt(d, eased, i);
            if (double.IsNaN(f)) continue;
            ctx.DrawEllipse(_cardFill, _currentRim, new Point(l.X(TimeAt(d, eased, i)), l.Y(f)), 6, 6);
            break;
        }
    }

    // On top of the contour: the floor's label on a card-coloured plate (so lines crossing it
    // don't hide it), the time axis and the legend.
    private void RenderTop(DrawingContext ctx)
    {
        if (Plot(Eased()) is not { } l) return;
        var soft = C("InkSoft");
        var floorLabel = Text($"register floor {l.Floor.ToString(CultureInfo.InvariantCulture)} Hz", 11, C("ZoneMascInk"));
        var floorLabelAt = new Point(l.W - PadR - floorLabel.Width - 3, l.Y(l.Floor) - 5 - floorLabel.Height);
        ctx.FillRectangle(new SolidColorBrush(C("Card"), 0.85),
            new Rect(floorLabelAt.X - 4, floorLabelAt.Y - 1, floorLabel.Width + 8, floorLabel.Height + 2), 4);
        ctx.DrawText(floorLabel, floorLabelAt);
        if (l.Zones is not null)
        {
            var end = Text(TimelineChart.Seconds(l.Start + l.Dur), 11, soft);
            ctx.DrawText(Text(TimelineChart.Seconds(l.Start), 11, soft), new Point(PadL, H - 18));
            ctx.DrawText(end, new Point(l.W - PadR - end.Width, H - 18));
            return;
        }
        ctx.DrawText(Text("time →", 11, soft), new Point(PadL, H - 18));
        var legend = Text("● dots = how each phrase landed", 11, soft);
        ctx.DrawText(legend, new Point(l.W - PadR - legend.Width, H - 18));
    }

    private readonly List<Point> _run = [];
    private Pen? _belowPen, _abovePen;
    private Color? _inkCard;
    private Pen? _divider, _cardRim, _currentRim;
    private IBrush? _femDot, _mascDot, _cardFill;

    /// <summary>The contour layer's other pens and brushes, rebuilt only when the theme changes.</summary>
    private void RefreshInks()
    {
        var card = C("Card");
        if (_inkCard == card && _divider is not null) return;
        _inkCard = card;
        _divider = new Pen(B("LineSoft"), 1);
        _cardRim = new Pen(B("Card"), 1.5);
        _currentRim = new Pen(B("InkStrong"), 2.5);
        _cardFill = B("Card");
        _femDot = new SolidColorBrush(ZoneColor(ZoneColorKey.Fem));
        _mascDot = new SolidColorBrush(ZoneColor(ZoneColorKey.Masc));
    }

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
