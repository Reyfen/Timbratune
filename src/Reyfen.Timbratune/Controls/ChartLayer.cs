using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// One layer of a chart (its background, its line, the labels on top), drawn by the chart and
/// redrawn on its own: while a live line glides only the line's layer redraws every frame, and the
/// compositor keeps the other layers' drawing as it was.
/// </summary>
internal sealed class ChartLayer(Action<DrawingContext> render) : Control
{
    public override void Render(DrawingContext context) => render(context);
}

/// <summary>A <see cref="ThemedControl"/> drawn as stacked <see cref="ChartLayer"/>s (bottom first), each filling it.</summary>
public abstract class LayeredChart : ThemedControl
{
    private readonly List<ChartLayer> _layers = [];

    /// <summary>Adds a layer above the ones added before.</summary>
    private protected ChartLayer AddLayer(Action<DrawingContext> render)
    {
        var layer = new ChartLayer(render);
        _layers.Add(layer);
        VisualChildren.Add(layer);
        return layer;
    }

    protected override void Redraw()
    {
        foreach (var layer in _layers) layer.InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var layer in _layers)
        {
            layer.Measure(finalSize);
            layer.Arrange(new Rect(finalSize));
        }
        return finalSize;
    }
}
