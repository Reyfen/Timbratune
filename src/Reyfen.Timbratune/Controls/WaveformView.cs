using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// Rounded-bar waveform with a progress overlay (the wavesurfer look from
/// WaveformPlayer.tsx: bar 2.5, gap 1.6, radius 3). Clicking runs
/// <see cref="SeekCommand"/> with the clicked fraction (0..1).
/// </summary>
public sealed class WaveformView : ThemedControl
{
    public static readonly StyledProperty<float[]?> PeaksProperty =
        AvaloniaProperty.Register<WaveformView, float[]?>(nameof(Peaks));
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Progress));
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<WaveformView, ICommand?>(nameof(SeekCommand));

    static WaveformView() => RedrawOn<WaveformView>(PeaksProperty, ProgressProperty);

    public float[]? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public ICommand? SeekCommand { get => GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    private const double H = 42, Bar = 2.5, Gap = 1.6;
    private double? _hoverX;

    public WaveformView()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, H);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width <= 0) return;
        var fraction = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        if (SeekCommand?.CanExecute(fraction) == true) SeekCommand.Execute(fraction);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        if (!IsOnScreen) return;
        using var perf = Diagnostics.Perf.Measure("render.WaveformView");
        var w = Bounds.Width;
        if (w <= 0) return;
        var peaks = Peaks;
        var bars = (int)(w / (Bar + Gap));
        var wave = B("Wave");
        var progress = B("WaveProgress");
        var progressX = Math.Clamp(Progress, 0, 1) * w;

        for (var i = 0; i < bars; i++)
        {
            var amp = peaks is { Length: > 0 } ? peaks[(int)((long)i * peaks.Length / bars)] : 0.08f;
            var h = Math.Max(2, amp * (H - 2));
            var x = i * (Bar + Gap);
            var rect = new Rect(x, (H - h) / 2, Bar, h);
            ctx.DrawRectangle(x + Bar <= progressX ? progress : wave, null, new RoundedRect(rect, Math.Min(3, Bar / 2)));
        }

        if (Progress > 0) ctx.FillRectangle(B("WaveCursor"), new Rect(Math.Min(progressX, w - 2), 0, 2, H));
        if (_hoverX is { } hx) ctx.FillRectangle(B("InkFaint", 0.6), new Rect(hx, 0, 1, H));
    }
}
