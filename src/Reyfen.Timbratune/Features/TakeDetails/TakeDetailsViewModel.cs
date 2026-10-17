using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Diagnostics;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.TakeDetails;

/// <summary>A take's detail file (contour, phrases, trends), read from the store.</summary>
public sealed class TakeDetailsManager(RecordingStore store)
{
    public RecordingDetail? LoadDetail(Recording take) => store.LoadDetail(take);

    /// <summary>Analyzed before the time trends existed (only the pitch-based trends can be rebuilt from it).</summary>
    public static bool HasOldTrends(RecordingDetail detail) => (detail.Trends?.Version ?? 0) < TakeTrends.CurrentVersion;
}

/// <summary>
/// Everything shown for one take: the stat cards, resonance, register and trends. Two of them:
/// the take in focus (<see cref="Active"/>), and while recording the take so far (<see cref="LiveTake"/>,
/// refreshed from the live session about once a second).
/// </summary>
public sealed partial class TakeDetailsViewModel : ObservableObject
{
    private static readonly IRelayCommand<MetricId> NoModal = new RelayCommand<MetricId>(_ => { });

    private readonly IActiveTakeService _activeTake;
    private readonly TakeDetailsManager _manager;
    private readonly IFeatureFlags _flags;
    private readonly IMessenger _messenger;
    private readonly MetricRegistry _registry;
    private bool _isLive;

    public TakeDetailsViewModel(IActiveTakeService active, ILiveSession session, TakeDetailsManager manager,
        IMetricCompareLauncher metricCompare, IFeatureFlags flags, IMessenger messenger, MetricRegistry registry)
    {
        _activeTake = active;
        _manager = manager;
        _flags = flags;
        _messenger = messenger;
        _registry = registry;
        OpenMetricCommand = new RelayCommand<MetricId>(metricCompare.Open);
        active.Changed += (_, _) => ShowActive();
        messenger.Register<TakeDetailsViewModel, TakeAnalysisUpdated>(this, (vm, m) =>
        {
            if (vm._activeTake.Active?.Id == m.TakeId) vm.ShowActive();
        });
        session.Started += (_, _) => _isLive = true;
        session.SnapshotReady += OnSnapshot;
        session.Ended += (_, _) =>
        {
            _isLive = false;
            LiveTake = null;
        };
    }

    /// <summary>Opens a metric's comparison (the stat cards and gauges).</summary>
    public IRelayCommand<MetricId> OpenMetricCommand { get; }

    /// <summary>The take in focus, or null when there are none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasActive))] private TakeViewModel? _active;

    /// <summary>The take so far while recording (and frozen while the saved take is analyzed); null otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLiveTake))] private TakeViewModel? _liveTake;

    public bool HasActive => Active is not null;
    public bool HasLiveTake => LiveTake is not null;

    private void ShowActive()
    {
        if (_activeTake.Active is not { } take)
        {
            Active = null;
            return;
        }
        RecordingDetail? detail;
        using (Perf.Measure("take.load")) detail = _manager.LoadDetail(take);
        using (Perf.Measure("take.viewmodel")) Active = new TakeViewModel(take, detail, _activeTake.IsLatest, OpenMetricCommand, _flags, _registry);
        if (detail is not null && TakeDetailsManager.HasOldTrends(detail)) _messenger.Send(new TakeTrendsOutdated(take.Id));
    }

    // Analysis thread: only full updates carry a result (about once a second, and the final one).
    private void OnSnapshot(LiveSnapshot snapshot)
    {
        if (snapshot.Result is not { } result || Perf.Flags.Contains("nolivetake") && !snapshot.IsFinal) return;
        Dispatcher.UIThread.Post(() => ShowLive(snapshot, result));
    }

    // The take's cards follow once a second: the live graphs carry the moment-to-moment feedback,
    // and refreshing the cards' texts and charts on every update kept a phone's UI thread busy.
    private void ShowLive(LiveSnapshot snapshot, AnalysisResult result)
    {
        if (!_isLive) return; // ended meanwhile
        using (Perf.Measure("live.takeview"))
        {
            // Updated in place: the view keeps its controls and only re-lays out what changed.
            if (LiveTake is { } take) take.Update(result.Metrics, result.Detail, snapshot.Elapsed);
            else LiveTake = new TakeViewModel(result.Metrics, result.Detail, isLatest: true, NoModal, _flags, _registry, liveElapsed: snapshot.Elapsed);
        }
    }
}

public static class TakeDetailsFeature
{
    public static IServiceCollection AddTakeDetailsFeature(this IServiceCollection services)
    {
        services.AddSingleton<TakeDetailsManager>();
        services.AddSingleton<TakeDetailsViewModel>();
        return services;
    }
}
