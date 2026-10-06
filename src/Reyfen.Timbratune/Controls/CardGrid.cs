using Avalonia;
using Avalonia.Controls;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// A responsive card grid: as many equal columns as fit at <see cref="MinItemWidth"/> or wider,
/// stretched to fill the row, with <see cref="Spacing"/> between cards. One full-width column
/// on a phone, several on a desktop window. Each row is as tall as its tallest card.
/// </summary>
public sealed class CardGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(MinItemWidth), 280);
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(Spacing), 12);

    static CardGrid() => AffectsMeasure<CardGrid>(MinItemWidthProperty, SpacingProperty);

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private (int Columns, double ItemWidth) Layout(double width)
    {
        var count = Children.Count(c => c.IsVisible);
        if (double.IsInfinity(width)) width = Math.Max(1, count) * (MinItemWidth + Spacing) - Spacing;
        var columns = Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
        columns = Math.Min(columns, Math.Max(1, count));
        return (columns, Math.Max(0, (width - (columns - 1) * Spacing) / columns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Layout(availableSize.Width);
        double height = 0, rowHeight = 0;
        var column = 0;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++column < columns) continue;
            height += rowHeight + Spacing;
            rowHeight = 0;
            column = 0;
        }
        if (column > 0) height += rowHeight + Spacing;
        var width = double.IsInfinity(availableSize.Width) ? columns * itemWidth + (columns - 1) * Spacing : availableSize.Width;
        return new Size(width, Math.Max(0, height - Spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        var visible = Children.Where(c => c.IsVisible).ToList();
        double y = 0;
        for (var start = 0; start < visible.Count; start += columns)
        {
            var row = visible.Skip(start).Take(columns).ToList();
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (itemWidth + Spacing), y, itemWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
