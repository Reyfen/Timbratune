using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Euphonia.Core.Analysis;
using Euphonia.Core.Audio;
using Euphonia.Core.Domain;
using Euphonia.Core.Models;
using Euphonia.Core.Storage;

namespace Euphonia.ViewModels;

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

    public RecordViewModel(IAudioRecorder recorder, IAnalysisEngine engine, RecordingStore store, Func<Task> onRecorded)
    {
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
        if (_lastSnapshot is { } snapshot && LiveTimelines is not null)
            LiveTimelines = new LiveTimelinesViewModel(snapshot, value?.Seconds);
    }

    [RelayCommand]
    private void Start()
    {
        try
        {
            _live = new LiveAnalyzer(_recorder.SampleRate);
            _recorder.SamplesCaptured += OnSamplesCaptured;
            _recorder.Start();
            _elapsed.Restart();
            _clock.Start();
            _lastSnapshot = null;
            LiveTimelines = new LiveTimelinesViewModel(LiveSnapshot.Empty, LiveWindow?.Seconds);
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
            var (snapshot, timelines) = await Task.Run(() =>
            {
                var s = live.Update();
                return (s, new LiveTimelinesViewModel(s, window));
            });
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

    private void Show(LiveSnapshot snapshot, LiveTimelinesViewModel? timelines = null)
    {
        _lastSnapshot = snapshot;
        LiveTimelines = timelines ?? new LiveTimelinesViewModel(snapshot, LiveWindow?.Seconds);
        if (snapshot.Result is { } result)
            LiveTake = new TakeViewModel(result.Metrics, result.Detail, isLatest: true, NoModal, liveElapsed: snapshot.Elapsed);
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _liveTimer.Stop();
        _clock.Stop();
        _elapsed.Stop();
        State = RecordState.Analyzing;
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
                Show(await Task.Run(() => live.Update(final: true, length: length)));
            }

            var result = await _engine.AnalyzeAsync(wav);
            var entry = result.Metrics;
            entry.Label = string.IsNullOrWhiteSpace(Label) ? "untitled take" : Label.Trim();
            entry.Note = "";
            entry.Date = DateTime.Now.ToString("yyyy-MM-dd");
            entry.SourceFile = Path.GetFileName(wav);
            var sourceWav = wav;
            await Task.Run(() => _store.Add(entry, result.Detail, sourceWav));

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
