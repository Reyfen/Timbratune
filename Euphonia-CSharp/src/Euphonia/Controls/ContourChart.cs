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

    static ContourChart() => RedrawOn<ContourChart>(DetailProperty, FemThresholdProperty);

    public RecordingDetail? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public double FemThreshold { get => GetValue(FemThresholdProperty); set => SetValue(FemThresholdProperty, value); }

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
        var dur = d.DurationS > 0 ? d.DurationS : t.Count > 0 && t[^1] > 0 ? t[^1] : 1;
        var fem = FemThreshold;

        var voiced = hz.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var maxHz = Math.Max(Math.Max(220, voiced.Count > 0 ? voiced.Max() : 0), fem) * 1.05;
        var minHz = Math.Min(Math.Min(80, floor - 20), voiced.Count > 0 ? voiced.Min() : double.MaxValue);

        double X(double time) => PadL + time / dur * iw;
        double Y(double f) => PadT + (1 - (f - minHz) / (maxHz - minHz)) * ih;

        ctx.FillRectangle(new SolidColorBrush(ZoneColor(ZoneColorKey.Fem), 0.12), new Rect(PadL, Y(maxHz), iw, Y(fem) - Y(maxHz)));
        ctx.FillRectangle(new SolidColorBrush(ZoneColor(ZoneColorKey.Masc), 0.22), new Rect(PadL, Y(floor), iw, Y(minHz) - Y(floor)));

        var floorPen = new Pen(new SolidColorBrush(Color.Parse("#7c9fd6")), 1.5) { DashStyle = new DashStyle([5 / 1.5, 4 / 1.5], 0) };
        ctx.DrawLine(floorPen, new Point(PadL, Y(floor)), new Point(w - PadR, Y(floor)));
        var floorLabel = Text($"register floor {floor.ToString(CultureInfo.InvariantCulture)} Hz", 11, C("ZoneMascInk"));
        ctx.DrawText(floorLabel, new Point(w - PadR - floorLabel.Width, Y(floor) - 5 - floorLabel.Height));

        var divider = new Pen(B("LineSoft"), 1);
        foreach (var p in d.Phrases) ctx.DrawLine(divider, new Point(X(p.End), PadT), new Point(X(p.End), PadT + ih));

        var soft = C("InkSoft");
        foreach (var v in new[] { minHz, floor, fem, maxHz }.Distinct().Where(v => v <= maxHz && v >= minHz))
        {
            var label = Text(Math.Round(v).ToString(CultureInfo.InvariantCulture), 10, soft);
            ctx.DrawText(label, new Point(PadL - 6 - label.Width, Y(v) - label.Height / 2));
        }

        // Contour runs, split on unvoiced gaps and on crossing the floor.
        var belowPen = new Pen(B("ZoneMascInk"), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var abovePen = new Pen(new SolidColorBrush(ZoneColor(ZoneColorKey.Fem), 0.9), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var run = new List<Point>();
        bool? runBelow = null;
        void Flush()
        {
            if (run.Count > 0 && runBelow is { } below) DrawRun(ctx, run, below ? belowPen : abovePen);
            run.Clear();
        }
        for (var i = 0; i < hz.Count && i < t.Count; i++)
        {
            if (hz[i] is not { } f)
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
            var at = new Point(X(p.End), PadT + ih + 14);
            ctx.DrawEllipse(new SolidColorBrush(ZoneColor(p.EndedInRegister ? ZoneColorKey.Fem : ZoneColorKey.Masc)), card, at, 4.5, 4.5);
            _dots.Add((at, $"phrase {k + 1}: ended {p.OffsetHz.ToString(CultureInfo.InvariantCulture)} Hz — " +
                           (p.EndedInRegister ? "landed in register 💕" : "fell out of register")));
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
