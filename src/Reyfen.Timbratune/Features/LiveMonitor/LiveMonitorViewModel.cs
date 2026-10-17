using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Diagnostics;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.LiveMonitor;

/// <summary>
/// The live graphs while recording (and frozen on the final state while the take is analyzed).
/// Each snapshot is smoothed on the analysis thread, where <see cref="ILiveSession.SnapshotReady"/>
/// is raised (as the app always did, on the live worker); only the finished frame goes to the UI
/// thread. Also the choice of how much of the take the graphs show.
/// </summary>
public sealed partial class LiveMonitorViewModel : ObservableObject
{
    private readonly ILiveSession _session;
    private readonly ISettings _settings;
    private readonly IReadOnlyList<LiveLine> _lines;
    // Read on the analysis thread: a boxed double? is replaced, never changed, so reads are whole.
    private volatile object? _windowSeconds;
    private long _chartsApplied;

    public LiveMonitorViewModel(ILiveSession session, ISettings settings, MetricRegistry registry)
    {
        _session = session;
        _settings = settings;
        _lines = LiveLine.From(registry);
        LiveWindow = LiveMonitorSettings.OptionFor(settings.Get(LiveMonitorSettings.WindowSeconds));
        session.Started += (_, _) =>
        {
            Timelines = new LiveTimelinesViewModel(_lines);
            Timelines.Apply(LiveTimelinesViewModel.Compute(LiveSnapshot.Empty, _lines, Window));
        };
        session.SnapshotReady += OnSnapshot;
        session.Ended += (_, _) => Timelines = null;
    }

    /// <summary>The graphs; null when nothing is live.</summary>
    [ObservableProperty] private LiveTimelinesViewModel? _timelines;

    /// <summary>How much of the take the live graphs show: all of it, or a sliding window of the last N s.</summary>
    [ObservableProperty] private LiveWindowOption _liveWindow = LiveWindowOption.All[0];

    public IReadOnlyList<LiveWindowOption> WindowOptions => LiveWindowOption.All;

    private double? Window => (double?)_windowSeconds;

    // Redraw straight away with the new window instead of waiting for the next audio.
    partial void OnLiveWindowChanged(LiveWindowOption value)
    {
        _windowSeconds = value?.Seconds;
        _settings.Set(LiveMonitorSettings.WindowSeconds, value?.Seconds ?? 0);
        // While recording the next update (≤ 100 ms) applies it: the snapshot's series lists are
        // refilled by the live analysis and are only read on its thread.
        if (_session.IsRecording) return;
        if (_session.Latest is { } snapshot && Timelines is { } timelines)
            timelines.Apply(LiveTimelinesViewModel.Compute(snapshot, _lines, value?.Seconds));
    }

    // Analysis thread.
    private void OnSnapshot(LiveSnapshot snapshot)
    {
        LiveTimelinesViewModel.Frame frame;
        using (Perf.Measure("live.compute")) frame = LiveTimelinesViewModel.Compute(snapshot, _lines, Window);
        Dispatcher.UIThread.Post(() => Apply(snapshot, frame));
    }

    private void Apply(LiveSnapshot snapshot, LiveTimelinesViewModel.Frame frame)
    {
        if (Timelines is not { } timelines) return; // ended meanwhile
        // Experiment (profiling builds): "slowcharts" moves the graphs at most every 200 ms.
        if (Perf.Flags.Contains("slowcharts") && !snapshot.IsFinal && Stopwatch.GetElapsedTime(_chartsApplied).TotalMilliseconds < 200) return;
        _chartsApplied = Stopwatch.GetTimestamp();
        using (Perf.Measure("live.apply")) timelines.Apply(frame);
    }
}

/// <summary>The live graphs' settings.</summary>
public static class LiveMonitorSettings
{
    /// <summary>How much of the take the live graphs show, in seconds; 0 = the whole take. Device-only for now (owner, 2026-10-15).</summary>
    public static readonly SettingKey<double> WindowSeconds =
        new("liveMonitor.windowSeconds", 0, SettingScope.Device, SettingsJson.Default.Double);

    /// <summary>The choice for a stored value; an unknown one (e.g. from a newer version) shows the whole take.</summary>
    public static LiveWindowOption OptionFor(double seconds) =>
        LiveWindowOption.All.FirstOrDefault(o => (o.Seconds ?? 0) == seconds) ?? LiveWindowOption.All[0];
}

public static class LiveMonitorFeature
{
    public static IServiceCollection AddLiveMonitorFeature(this IServiceCollection services)
    {
        services.AddSingleton<ISettingKey>(LiveMonitorSettings.WindowSeconds);
        services.AddSingleton<LiveMonitorViewModel>();
        return services;
    }
}
