using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Diagnostics;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.ViewModels;

public enum RecordState { Idle, Recording, Analyzing, Error }

/// <summary>
/// RecordButton.tsx: label → record → stop &amp; analyze → saved. While recording, a
/// <see cref="LiveAnalyzer"/> follows the microphone and the take view fills in
/// live (about 10 updates a second). On Stop the live views freeze, the WAV is
/// trimmed by &lt; 10 ms so its pitch frames line up with the live ones, and the
/// full analysis of the file becomes the saved take.
/// </summary>
public sealed partial class RecordViewModel : ObservableObject
{
    private static readonly IRelayCommand<MetricKey> NoModal = new RelayCommand<MetricKey>(_ => { });

    private readonly IAudioRecorder _recorder;
    private readonly IAnalysisEngine _engine;
    private readonly RecordingStore _store;
    private readonly Func<Task> _onRecorded;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _elapsed = new();
    private LiveAnalyzer? _live;
    private bool _liveBusy;

    private readonly Func<Task<bool>>? _requestMicrophone;

    /// <param name="requestMicrophone">
    /// Platforms that ask for microphone access at run time (Android) pass this: it asks if
    /// needed and returns whether recording is allowed. Desktop platforms pass null.
    /// </param>
    public RecordViewModel(IAudioRecorder recorder, IAnalysisEngine engine, RecordingStore store, Func<Task> onRecorded,
        Func<Task<bool>>? requestMicrophone = null)
    {
        _requestMicrophone = requestMicrophone;
        _recorder = recorder;
        _engine = engine;
        _store = store;
        _onRecorded = onRecorded;
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => OnPropertyChanged(nameof(ElapsedText)));
        _liveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, async (_, _) => await LiveTickAsync());
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRecording), nameof(IsAnalyzing), nameof(IsError))]
    private RecordState _state = RecordState.Idle;

    [ObservableProperty] private string _label = "";

    /// <summary>How far the work after Stop has got (0–100): finishing the live analysis, the full analysis, saving.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalyzingPercent), nameof(AnalyzingEstimate))]
    private double _analysisProgress;

    private readonly Stopwatch _analysisClock = new();
    private readonly Stopwatch _sinceLastLive = new();

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

    /// <summary>The take so far, while recording (and frozen while the saved take is analyzed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    private TakeViewModel? _liveTake;

    [ObservableProperty] private LiveTimelinesViewModel? _liveTimelines;

    /// <summary>How much of the take the live graphs show: all of it, or a sliding window of the last N s.</summary>
    [ObservableProperty] private LiveWindowOption _liveWindow = LiveWindowOption.All[0];

    public IReadOnlyList<LiveWindowOption> WindowOptions => LiveWindowOption.All;
    private LiveSnapshot? _lastSnapshot;

    public bool IsLive => LiveTake is not null || LiveTimelines is not null;
    public bool IsIdle => State == RecordState.Idle;
    public bool IsRecording => State == RecordState.Recording;
    public bool IsAnalyzing => State == RecordState.Analyzing;
    public bool IsError => State == RecordState.Error;
    public string ElapsedText => $"{_elapsed.Elapsed:m\\:ss}";

    /// <summary>Shown under the panel when the analysis engine can't run (recording still works).</summary>
    public string? EngineWarning => _engine.IsAvailable ? null : "⚠️ " + _engine.UnavailableReason;

    partial void OnLiveTimelinesChanged(LiveTimelinesViewModel? value) => OnPropertyChanged(nameof(IsLive));

    // Redraw straight away with the new window instead of waiting for the next audio.
    partial void OnLiveWindowChanged(LiveWindowOption value)
    {
        if (_lastSnapshot is { } snapshot && LiveTimelines is { } timelines)
            timelines.Apply(LiveTimelinesViewModel.Compute(snapshot, value?.Seconds));
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        try
        {
            if (_requestMicrophone is not null && !await _requestMicrophone())
            {
                ErrorDetail = "Timbratune needs microphone access to record 🎙️ — allow it when asked, or in the system settings.";
                State = RecordState.Error;
                return;
            }
            _live = new LiveAnalyzer(_recorder.SampleRate);
            _recorder.SamplesCaptured += OnSamplesCaptured;
            _recorder.Start();
            _elapsed.Restart();
            _clock.Start();
            _lastSnapshot = null;
            LiveTimelines = new LiveTimelinesViewModel();
            LiveTimelines.Apply(LiveTimelinesViewModel.Compute(LiveSnapshot.Empty, LiveWindow?.Seconds));
            _liveTimer.Start();
            State = RecordState.Recording;
        }
        catch (Exception ex)
        {
            Fail("couldn't open the microphone", ex);
        }
    }

    // Audio thread: copy the block into the live signal (quantized exactly as the WAV stores it).
    private void OnSamplesCaptured(ReadOnlySpan<float> samples) => _live?.AppendCaptured(samples);

    private async Task LiveTickAsync()
    {
        if (_liveBusy || _live is not { } live || State != RecordState.Recording) return;
        _liveBusy = true;
        try
        {
            var window = LiveWindow?.Seconds;
            if (Perf.Flags.Contains("liveslow") && _sinceLastLive.IsRunning && _sinceLastLive.ElapsedMilliseconds < 500) return;
            _sinceLastLive.Restart();
            // The whole-take statistics are only needed when the take's cards refresh.
            var full = LiveTake is null || Stopwatch.GetElapsedTime(_liveTakeShown) >= LiveTakeInterval;
            Func<(LiveSnapshot, LiveTimelinesViewModel.Frame)> work = () =>
            {
                LiveSnapshot s;
                using (Perf.Measure(full ? "live.update" : "live.update-quick")) s = live.Update(full: full);
                using (Perf.Measure("live.compute")) return (s, LiveTimelinesViewModel.Compute(s, window));
            };
            var (snapshot, timelines) = await Task.Run(work);
            if (State == RecordState.Recording) Show(snapshot, timelines);
        }
        catch (Exception)
        {
            // A failed live update only skips one refresh; the saved analysis is unaffected.
        }
        finally
        {
            _liveBusy = false;
        }
    }

    private void Show(LiveSnapshot snapshot, LiveTimelinesViewModel.Frame? frame = null)
    {
        _lastSnapshot = snapshot;
        using (Perf.Measure("live.apply")) LiveTimelines?.Apply(frame ?? LiveTimelinesViewModel.Compute(snapshot, LiveWindow?.Seconds));
        if (snapshot.Result is not { } result || Perf.Flags.Contains("nolivetake") && !snapshot.IsFinal) return;
        // The take's cards follow once a second (only full updates carry a result): the live
        // graphs above carry the moment-to-moment feedback, and refreshing the cards' texts and
        // charts on every update kept a phone's UI thread busy most of the time.
        _liveTakeShown = Stopwatch.GetTimestamp();
        using (Perf.Measure("live.takeview"))
        {
            // Updated in place: the view keeps its controls and only re-lays out what changed.
            if (LiveTake is { } take) take.Update(result.Metrics, result.Detail, snapshot.Elapsed);
            else LiveTake = new TakeViewModel(result.Metrics, result.Detail, isLatest: true, NoModal, liveElapsed: snapshot.Elapsed);
        }
    }

    /// <summary>How often the live take's cards are rebuilt while recording.</summary>
    private static readonly TimeSpan LiveTakeInterval = TimeSpan.FromSeconds(1);
    private long _liveTakeShown;

    [RelayCommand]
    private async Task StopAsync()
    {
        _liveTimer.Stop();
        _clock.Stop();
        _elapsed.Stop();
        AnalysisProgress = 0;
        _analysisClock.Restart();
        State = RecordState.Analyzing;
        // Shares of the time after Stop, measured on a 27.6 s take: the last live update, the analysis, saving.
        const double liveShare = 0.15, analysisShare = 0.80;
        string? wav = null;
        try
        {
            wav = await _recorder.StopAsync(); // after this no more samples arrive
            _recorder.SamplesCaptured -= OnSamplesCaptured;

            // Trim the take so its pitch frames sit exactly on the live ones, then freeze the
            // live view on the final live state while the saved analysis runs.
            var live = _live;
            if (live is not null)
            {
                while (_liveBusy) await Task.Delay(10);
                var recorded = (int)((new FileInfo(wav).Length - 44) / 2);
                var length = live.AlignedLength(Math.Min(recorded, live.SampleCount));
                WavWriter.Truncate(wav, length);
                var finalStart = Stopwatch.GetTimestamp();
                Show(await Task.Run(() => live.Update(final: true, length: length)));
                Perf.Report("stop.final-live", Stopwatch.GetElapsedTime(finalStart).TotalMilliseconds);
            }
            AnalysisProgress = 100 * liveShare;

            var analyzeStart = Stopwatch.GetTimestamp();
            var result = await _engine.AnalyzeAsync(wav,
                // Max: a late-arriving report must not move the bar back.
                progress: new ThrottledProgress(f => AnalysisProgress = Math.Max(AnalysisProgress, 100 * (liveShare + analysisShare * f))));
            AnalysisProgress = 100 * (liveShare + analysisShare);
            Perf.Report("stop.analyze", Stopwatch.GetElapsedTime(analyzeStart).TotalMilliseconds);
            var entry = result.Metrics;
            entry.Label = string.IsNullOrWhiteSpace(Label) ? "untitled take" : Label.Trim();
            entry.Note = "";
            entry.Date = DateTime.Now.ToString("yyyy-MM-dd");
            entry.SourceFile = Path.GetFileName(wav);
            var sourceWav = wav;
            using (Perf.Measure("stop.save")) await Task.Run(() => _store.Add(entry, result.Detail, sourceWav));
            AnalysisProgress = 100;

            Label = "";
            State = RecordState.Idle;
            await _onRecorded();
            ClearLive();
        }
        catch (Exception ex)
        {
            Fail("couldn't save that take", ex);
        }
        finally
        {
            if (wav is not null) try { File.Delete(wav); } catch (IOException) { }
        }
    }

    [RelayCommand]
    private void Retry()
    {
        ErrorDetail = null;
        State = RecordState.Idle;
    }

    private void ClearLive()
    {
        _live = null;
        _lastSnapshot = null;
        LiveTake = null;
        LiveTimelines = null;
    }

    private void Fail(string what, Exception ex)
    {
        _liveTimer.Stop();
        _recorder.SamplesCaptured -= OnSamplesCaptured;
        _recorder.Cancel();
        _clock.Stop();
        ClearLive();
        ErrorDetail = $"{what} 🌧️ — {ex.Message}";
        State = RecordState.Error;
    }
}

/// <summary>
/// Progress that reaches the UI thread at most every half percent: the analysis
/// reports hundreds of times a second, from worker threads.
/// </summary>
internal sealed class ThrottledProgress : IProgress<double>
{
    private readonly Action<double> _apply;
    private readonly Lock _gate = new();
    private double _sent = -1;

    /// <summary>Create on the UI thread: <paramref name="apply"/> runs there.</summary>
    public ThrottledProgress(Action<double> apply) => _apply = apply;

    public void Report(double value)
    {
        lock (_gate)
        {
            if (value < 1 && value - _sent < 0.005) return;
            _sent = value;
        }
        Dispatcher.UIThread.Post(() => _apply(value));
    }
}
