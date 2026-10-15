using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.Record;

public enum RecordState { Idle, Recording, Analyzing, Error }

/// <summary>
/// RecordButton.tsx: label → record → stop &amp; analyze → saved. The work is the
/// <see cref="RecordingManager"/>'s; this is the panel: the label, the clock, the analysis
/// progress and errors. The live graphs and the live take follow the manager's live session.
/// </summary>
public sealed partial class RecordViewModel : ObservableObject
{
    private readonly RecordingManager _manager;
    private readonly DispatcherTimer _clock;
    private readonly Stopwatch _elapsed = new();
    private readonly Stopwatch _analysisClock = new();

    public RecordViewModel(RecordingManager manager)
    {
        _manager = manager;
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => OnPropertyChanged(nameof(ElapsedText)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRecording), nameof(IsAnalyzing), nameof(IsError))]
    private RecordState _state = RecordState.Idle;

    [ObservableProperty] private string _label = "";

    /// <summary>How far the work after Stop has got (0–100): finishing the live analysis, the full analysis, saving.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalyzingPercent), nameof(AnalyzingEstimate))]
    private double _analysisProgress;

    /// <summary>"42%".</summary>
    public string AnalyzingPercent => $"{AnalysisProgress:0}%";

    /// <summary>"· about 3 s left 💗" once there is enough to go on, otherwise just the heart.</summary>
    public string AnalyzingEstimate
    {
        get
        {
            var fraction = AnalysisProgress / 100;
            var elapsed = _analysisClock.Elapsed.TotalSeconds;
            if (fraction is >= 0.15 and < 1 && elapsed >= 0.5)
            {
                var left = elapsed / fraction * (1 - fraction);
                return (left < 1.5 ? "· almost done" : $"· about {Math.Round(left):0} s left") + " 💗";
            }
            return "💗";
        }
    }

    [ObservableProperty] private string? _errorDetail;

    /// <summary>
    /// What the Shell puts in the panel: next to the record button while idle (the Import button),
    /// the live-window choice (while idle and while recording), and at the bottom (what an import is
    /// doing). Plain objects, shown through the view locator: the panel doesn't know the features
    /// that fill them.
    /// </summary>
    [ObservableProperty] private object? _idleActions;

    [ObservableProperty] private object? _windowPicker;

    [ObservableProperty] private object? _footer;

    public bool IsIdle => State == RecordState.Idle;
    public bool IsRecording => State == RecordState.Recording;
    public bool IsAnalyzing => State == RecordState.Analyzing;
    public bool IsError => State == RecordState.Error;
    public string ElapsedText => $"{_elapsed.Elapsed:m\\:ss}";

    /// <summary>Shown under the panel when the analysis engine can't run (recording still works).</summary>
    public string? EngineWarning => _manager.EngineWarning;

    [RelayCommand]
    private async Task StartAsync()
    {
        try
        {
            if (!await _manager.StartAsync())
            {
                ErrorDetail = "Timbratune needs microphone access to record 🎙️ — allow it when asked, or in the system settings.";
                State = RecordState.Error;
                return;
            }
            _elapsed.Restart();
            _clock.Start();
            State = RecordState.Recording;
        }
        catch (Exception ex)
        {
            Fail("couldn't open the microphone", ex);
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _clock.Stop();
        _elapsed.Stop();
        AnalysisProgress = 0;
        _analysisClock.Restart();
        State = RecordState.Analyzing;
        try
        {
            // Max: a late-arriving report must not move the bar back.
            await _manager.StopAsync(Label, new ThrottledProgress(f => AnalysisProgress = Math.Max(AnalysisProgress, 100 * f)));
            AnalysisProgress = 100;
            Label = "";
            State = RecordState.Idle;
            await _manager.FinishAsync();
        }
        catch (Exception ex)
        {
            Fail("couldn't save that take", ex);
        }
    }

    [RelayCommand]
    private void Retry()
    {
        ErrorDetail = null;
        State = RecordState.Idle;
    }

    private void Fail(string what, Exception ex)
    {
        _clock.Stop();
        _manager.Cancel();
        ErrorDetail = $"{what} 🌧️ — {ex.Message}";
        State = RecordState.Error;
    }
}

public static class RecordingFeature
{
    public static IServiceCollection AddRecordingFeature(this IServiceCollection services)
    {
        services.AddSingleton<RecordingManager>();
        services.AddSingleton<ILiveSession>(sp => sp.GetRequiredService<RecordingManager>());
        services.AddSingleton<RecordViewModel>();
        return services;
    }
}
