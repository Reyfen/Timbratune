using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

public enum TickKind { Take, Fem, Masc }

/// <summary>One marker on the reference scale — a take (above the band) or a reference voice (below).</summary>
public sealed record ScaleTick(
    string Key,
    double Value,
    double Pct,
    string Label,
    TickKind Kind,
    bool Active,
    int Lane,
    string? AudioPath,
    string PlayTitle,
    string Tooltip);

/// <summary>
/// The scale inside MetricModal.tsx: take dots stacked in lanes above a
/// zone band, reference-voice ticks below it, and the lo/hi axis. Clicking a
/// marker with audio runs <see cref="TickCommand"/> with the tick.
/// </summary>
public sealed class MetricScale : ThemedControl
{
    public static readonly StyledProperty<IReadOnlyList<Zone>?> ZonesProperty =
        AvaloniaProperty.Register<MetricScale, IReadOnlyList<Zone>?>(nameof(Zones));
    public static readonly StyledProperty<double> LoProperty =
        AvaloniaProperty.Register<MetricScale, double>(nameof(Lo));
    public static readonly StyledProperty<double> HiProperty =
        AvaloniaProperty.Register<MetricScale, double>(nameof(Hi), 1);
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<MetricScale, string>(nameof(Unit), "");
    public static readonly StyledProperty<IReadOnlyList<ScaleTick>?> TakesProperty =
        AvaloniaProperty.Register<MetricScale, IReadOnlyList<ScaleTick>?>(nameof(Takes));
    public static readonly StyledProperty<IReadOnlyList<ScaleTick>?> RefsProperty =
        AvaloniaProperty.Register<MetricScale, IReadOnlyList<ScaleTick>?>(nameof(Refs));
    public static readonly StyledProperty<string?> SelectedKeyProperty =
        AvaloniaProperty.Register<MetricScale, string?>(nameof(SelectedKey));
    public static readonly StyledProperty<ICommand?> TickCommandProperty =
        AvaloniaProperty.Register<MetricScale, ICommand?>(nameof(TickCommand));

    static MetricScale()
    {
        RedrawOn<MetricScale>(ZonesProperty, LoProperty, HiProperty, UnitProperty, TakesProperty, RefsProperty, SelectedKeyProperty);
        AffectsMeasure<MetricScale>(TakesProperty, RefsProperty);
    }

    public IReadOnlyList<Zone>? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }
    public double Lo { get => GetValue(LoProperty); set => SetValue(LoProperty, value); }
    public double Hi { get => GetValue(HiProperty); set => SetValue(HiProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public IReadOnlyList<ScaleTick>? Takes { get => GetValue(TakesProperty); set => SetValue(TakesProperty, value); }
    public IReadOnlyList<ScaleTick>? Refs { get => GetValue(RefsProperty); set => SetValue(RefsProperty, value); }
    public string? SelectedKey { get => GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public ICommand? TickCommand { get => GetValue(TickCommandProperty); set => SetValue(TickCommandProperty, value); }

    // Geometry constants from MetricModal.tsx.
    private const double TakeRow = 22, TakeGap = 6, DotR = 8;
    private const double BandH = 30, RefRowH = 26, AxisH = 20, SideInset = 14;

    private readonly List<(Rect Hit, ScaleTick Tick)> _hits = [];
    private ScaleTick? _hover;

    private int Lanes => (Takes ?? []).Select(t => t.Lane).DefaultIfEmpty(-1).Max() + 1;
    private double TakesHeight => TakeGap + DotR + Math.Max(1, Lanes) * TakeRow;
    private bool HasRefs => Refs is { Count: > 0 };

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
            TakesHeight + BandH + (HasRefs ? RefRowH : 6) + AxisH);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit == _hover) return;
        _hover = hit;
        ToolTip.SetTip(this, hit?.Tooltip);
        Cursor = hit?.AudioPath is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is { AudioPath: not null } tick && TickCommand?.CanExecute(tick) == true)
        {
            TickCommand.Execute(tick);
            e.Handled = true;
        }
    }

    private ScaleTick? HitTest(Point p) =>
        _hits.LastOrDefault(h => h.Hit.Contains(p)).Tick;

    public override void Render(DrawingContext ctx)
    {
        _hits.Clear();
        var w = Bounds.Width - 2 * SideInset;
        var span = Hi - Lo;
        if (w <= 0 || span <= 0) return;
        double X(double pct) => SideInset + pct / 100 * w;

        var bandTop = TakesHeight;

        // zone band
        using (ctx.PushClip(new RoundedRect(new Rect(SideInset, bandTop, w, BandH), 10)))
        {
            ctx.FillRectangle(B("LineSoft"), new Rect(SideInset, bandTop, w, BandH));
            foreach (var z in Zones ?? [])
            {
                var left = Math.Clamp((z.From - Lo) / span * 100, 0, 100);
                var right = Math.Clamp((z.To - Lo) / span * 100, 0, 100);
                if (right <= left) continue;
                var rect = new Rect(X(left), bandTop, X(right) - X(left), BandH);
                ctx.FillRectangle(new SolidColorBrush(ZoneColor(z.Color)), rect);
                var name = Text(z.Name, 11, C("OnZone"), FontWeight.SemiBold);
                if (name.Width + 6 < rect.Width)
                    ctx.DrawText(name, new Point(rect.X + (rect.Width - name.Width) / 2, rect.Y + (BandH - name.Height) / 2));
            }
        }

        // takes: dot + label per lane, dashed guide down to the band
        var ink = C("InkStrong");
        var guide = new Pen(B("InkFaint"), 1) { DashStyle = new DashStyle([3, 3], 0) };
        foreach (var t in (Takes ?? []).OrderBy(t => t.Active))
        {
            var x = X(t.Pct);
            var dotY = bandTop - TakeGap - t.Lane * TakeRow - DotR;
            ctx.DrawLine(guide, new Point(x, dotY + DotR), new Point(x, bandTop));
            var playing = t.Key == SelectedKey;
            var hovered = _hover == t;
            var fill = t.Active || playing ? B("AccentEmphasis") : B("Accent");
            var ring = new Pen(hovered || playing ? B("InkStrong") : B("Card"), hovered || playing ? 2.5 : 2);
            ctx.DrawEllipse(fill, ring, new Point(x, dotY), DotR - 1, DotR - 1);
            var label = Text(t.Label, 11, ink, t.Active ? FontWeight.Bold : FontWeight.SemiBold);
            ctx.DrawText(label, new Point(x + DotR + 2, dotY - label.Height / 2));
            _hits.Add((new Rect(x - DotR - 2, dotY - DotR - 2, DotR * 2 + 6 + label.Width, DotR * 2 + 4), t));
        }

        // reference ticks under the band
        var refTop = bandTop + BandH + 2;
        if (HasRefs)
        {
            foreach (var t in Refs!)
            {
                var x = X(t.Pct);
                var color = ZoneColor(t.Kind == TickKind.Fem ? ZoneColorKey.Fem : ZoneColorKey.Masc);
                var playing = t.Key == SelectedKey;
                var hovered = _hover == t;
                var stem = new Rect(x - 2, refTop, 4, 14);
                ctx.DrawRectangle(new SolidColorBrush(color), playing || hovered ? new Pen(B("InkStrong"), 1.5) : null,
                    new RoundedRect(stem, 2));
                if (t.AudioPath is not null) ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(x, refTop + 19), 2.5, 2.5);
                _hits.Add((new Rect(x - 5, refTop - 2, 10, RefRowH), t));
            }
        }

        // axis
        var axisY = refTop + (HasRefs ? RefRowH - 2 : 4);
        var soft = C("InkSoft");
        var lo = Text(Math.Round(Lo).ToString(CultureInfo.InvariantCulture) + Unit, 11, soft);
        var hi = Text(Math.Round(Hi).ToString(CultureInfo.InvariantCulture) + Unit, 11, soft);
        ctx.DrawText(lo, new Point(SideInset, axisY));
        ctx.DrawText(hi, new Point(SideInset + w - hi.Width, axisY));
    }
}
