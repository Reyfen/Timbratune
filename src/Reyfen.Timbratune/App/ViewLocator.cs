using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Reyfen.Timbratune.ViewModels;
using Reyfen.Timbratune.Views;

namespace Reyfen.Timbratune.App;

/// <summary>
/// Which view shows which view model: a hand-written map, no reflection or name matching, so it
/// is trim/AOT safe and costs nothing at start-up. Registered as an application data template,
/// so a <c>ContentControl</c> whose content is one of these view models shows its view.
/// A feature adds its pairs here when it gets its own views.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _views = new()
    {
        [typeof(MainViewModel)] = () => new MainView(),
        [typeof(TakeViewModel)] = () => new TakeView(),
        [typeof(LiveTimelinesViewModel)] = () => new LiveTimelinesView(),
        [typeof(MetricModalViewModel)] = () => new MetricModalView(),
    };

    public bool Match(object? data) => data is not null && _views.ContainsKey(data.GetType());

    /// <summary>The view for <paramref name="data"/>, with it as its DataContext.</summary>
    public Control Build(object? data)
    {
        if (data is null || !_views.TryGetValue(data.GetType(), out var create))
            return new TextBlock { Text = $"no view for {data?.GetType().Name ?? "null"}" };
        var view = create();
        view.DataContext = data;
        return view;
    }
}
