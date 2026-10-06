using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// A TextBlock drawn in a zone's text colour (Zone…TextColor in Palette.axaml), e.g. a live value in the colour of the
/// band its dot sits in. With no zone it keeps its styled foreground. Follows theme changes.
/// </summary>
public sealed class ZoneText : TextBlock
{
    public static readonly StyledProperty<ZoneColorKey?> ZoneColorProperty =
        AvaloniaProperty.Register<ZoneText, ZoneColorKey?>(nameof(ZoneColor));

    public ZoneColorKey? ZoneColor { get => GetValue(ZoneColorProperty); set => SetValue(ZoneColorProperty, value); }

    public ZoneText() => ActualThemeVariantChanged += (_, _) => Recolor();

    // Styled as a TextBlock, so TextBlock classes (e.g. "live-value") apply.
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoneColorProperty) Recolor();
    }

    private void Recolor()
    {
        if (ZoneColor is { } key &&
            this.TryFindResource(ThemedControl.ZoneKey(key) + "TextColor", ActualThemeVariant, out var v) && v is Color c)
            Foreground = new SolidColorBrush(c);
        else
            ClearValue(ForegroundProperty);
    }
}
