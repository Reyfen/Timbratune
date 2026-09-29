using Avalonia;
using Avalonia.Media;
using Euphonia.Core.Domain;

namespace Euphonia.Controls;

/// <summary>The little zone-coloured name pill ("fem", "bright", "steady"…).</summary>
public sealed class ZonePill : ThemedControl
{
    public static readonly StyledProperty<Zone?> ZoneProperty =
        AvaloniaProperty.Register<ZonePill, Zone?>(nameof(Zone));

    static ZonePill()
    {
        RedrawOn<ZonePill>(ZoneProperty);
        AffectsMeasure<ZonePill>(ZoneProperty);
    }

    public Zone? Zone { get => GetValue(ZoneProperty); set => SetValue(ZoneProperty, value); }

    private FormattedText? Label => Zone is { } z ? Text(z.Name, 11, C("OnZone"), FontWeight.Bold) : null;

    protected override Size MeasureOverride(Size availableSize) =>
        Label is { } t ? new Size(t.Width + 18, 20) : default;

    public override void Render(DrawingContext ctx)
    {
        if (Zone is not { } z || Label is not { } t) return;
        ctx.DrawRectangle(new SolidColorBrush(ZoneColor(z.Color)), null, new RoundedRect(new Rect(Bounds.Size), 10));
        ctx.DrawText(t, new Point((Bounds.Width - t.Width) / 2, (Bounds.Height - t.Height) / 2));
    }
}
