using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Reyfen.Timbratune.Features.Import;
using Reyfen.Timbratune.Features.LiveMonitor;
using Reyfen.Timbratune.Features.MetricCompare;
using Reyfen.Timbratune.Features.TakeActions;
using Reyfen.Timbratune.Features.TakeDetails;
using Reyfen.Timbratune.Shell;

namespace Reyfen.Timbratune.App;

/// <summary>
/// Which view shows which view model: a hand-written map, no reflection or name matching, so it
/// is trim/AOT safe and costs nothing at start-up. Registered as an application data template,
/// so a <c>ContentControl</c> whose content is one of these view models shows its view (the
/// overlay stack's front, the record panel's slots). A feature adds its pairs here.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _views = new()
    {
        [typeof(ShellViewModel)] = () => new ShellView(),
        [typeof(TakeViewModel)] = () => new TakeView(),
        [typeof(LiveTimelinesViewModel)] = () => new LiveTimelinesView(),
        [typeof(LiveMonitorViewModel)] = () => new LiveWindowPickerView(),
        [typeof(MetricModalViewModel)] = () => new MetricModalView(),
        [typeof(TakeDialogViewModel)] = () => new TakeDialogView(),
        [typeof(ImportViewModel)] = () => new ImportButtonView(),
        [typeof(ImportStatusViewModel)] = () => new ImportStatusView(),
        [typeof(FfmpegOfferViewModel)] = () => new FfmpegOfferView(),
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
