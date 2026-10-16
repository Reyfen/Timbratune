using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Diagnostics;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;
using Reyfen.Timbratune.Shared.Telemetry;

namespace Reyfen.Timbratune.Features.Record;

/// <summary>
/// Recording a take: the microphone, the live analysis that follows it, and Stop: the WAV is
/// trimmed by &lt; 10 ms so its pitch frames line up with the live ones, then analyzed in full
/// and saved. It is the <see cref="ILiveSession"/> the live views follow.
/// </summary>
public sealed class RecordingManager : ILiveSession
{
    /// <summary>One update starts at most this often, on the analysis thread.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    /// <summary>How often a full update (whole-take statistics, for the live take's cards) runs.</summary>
    private static readonly TimeSpan FullInterval = TimeSpan.FromSeconds(1);

    private readonly IAudioRecorder _recorder;
    private readonly IAnalysisEngine _engine;
    private readonly RecordingStore _store;
    private readonly IAnalysisScheduler _scheduler;
    private readonly IPermissions _permissions;
    private readonly ITakeCatalog _catalog;
    private readonly IActiveTakeService _active;
    private readonly IMessenger _messenger;
    private readonly IAnalytics _analytics;
    private readonly ICrashReporter _crashes;
    private readonly IAppLog _log;
    private LiveAnalyzer? _live;
    private Task _loop = Task.CompletedTask;
    private CancellationTokenSource? _loopStop;
    private int? _savedId;

    public RecordingManager(IAudioRecorder recorder, IAnalysisEngine engine, RecordingStore store, IAnalysisScheduler scheduler,
        IPermissions permissions, ITakeCatalog catalog, IActiveTakeService active, IMessenger messenger,
        IAnalytics? analytics = null, ICrashReporter? crashes = null, IAppLog? log = null)
    {
        _analytics = analytics ?? NullAnalytics.Instance;
        _crashes = crashes ?? NullCrashReporter.Instance;
        _log = log ?? NullAppLog.Instance;
        _recorder = recorder;
        _engine = engine;
        _store = store;
        _scheduler = scheduler;
        _permissions = permissions;
        _catalog = catalog;
        _active = active;
        _messenger = messenger;
    }

    /// <summary>Shown under the panel when the analysis engine can't run (recording still works).</summary>
    public string? EngineWarning => _engine.IsAvailable ? null : "⚠️ " + _engine.UnavailableReason;

    // Read on the analysis thread.
    private volatile bool _isRecording;

    public bool IsLive { get; private set; }
    public bool IsRecording { get => _isRecording; private set => _isRecording = value; }
    public LiveSnapshot? Latest { get; private set; }

    public event Action<LiveSnapshot>? SnapshotReady;
    public event EventHandler? Started;
    public event EventHandler? Ended;

    /// <summary>Starts recording (UI thread); false when the microphone isn't allowed. Throws if it can't open.</summary>
    public async Task<bool> StartAsync()
    {
        if (!await _permissions.RequestMicrophoneAsync()) return false;
        _live = new LiveAnalyzer(_recorder.SampleRate);
        _recorder.SamplesCaptured += OnSamplesCaptured;
        _recorder.Start();
        _log.Log(AppLogLevel.Info, "recording", $"started at {_recorder.SampleRate} Hz");
        Latest = null;
        IsLive = IsRecording = true;
        Started?.Invoke(this, EventArgs.Empty);
        _messenger.Send(new RecordingStarted());
        _loopStop = new CancellationTokenSource();
        _loop = RunLiveAsync(_live, _loopStop.Token);
        return true;
    }

    // Audio thread: copy the block into the live signal (quantized exactly as the WAV stores it).
    private void OnSamplesCaptured(ReadOnlySpan<float> samples) => _live?.AppendCaptured(samples);

    /// <summary>
    /// The live updates. They run on the scheduler's live thread (one low-priority thread with its
    /// loops limited to a few cores on the native heads), starting at most once per 100 ms tick, so
    /// the UI and the renderer always have CPU left: running them back to back on every core made a
    /// Pixel 9 drop frames while recording (51 → 54.6 fps, frames over 50 ms 21 → 9 per 30 s).
    /// After a slow update the next one waits for the next tick, as with the UI timer this replaced.
    /// The whole-take statistics are only computed about once a second (the live take's cards).
    /// </summary>
    private async Task RunLiveAsync(LiveAnalyzer live, CancellationToken stop)
    {
        var period = Stopwatch.Frequency * Tick.Ticks / TimeSpan.TicksPerSecond;
        var nextTick = Stopwatch.GetTimestamp() + period;
        long lastFull = 0, lastStart = 0;
        var hadResult = false;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                // The next tick still ahead: after a slow update that is the next one, not straight away.
                var now = Stopwatch.GetTimestamp();
                while (nextTick <= now) nextTick += period;
                await Task.Delay(Stopwatch.GetElapsedTime(now, nextTick), stop).ConfigureAwait(false);
                now = Stopwatch.GetTimestamp();
                // Experiment (profiling builds): "liveslow" updates at most every 500 ms.
                if (Perf.Flags.Contains("liveslow") && lastStart != 0 && Stopwatch.GetElapsedTime(lastStart).TotalMilliseconds < 500) continue;
                lastStart = now;
                var full = !hadResult || Stopwatch.GetElapsedTime(lastFull) >= FullInterval;
                if (full) lastFull = now;
                try
                {
                    var hasResult = await _scheduler.RunLiveAsync(() =>
                    {
                        LiveSnapshot snapshot;
                        using (Perf.Measure(full ? "live.update" : "live.update-quick")) snapshot = live.Update(full: full);
                        if (IsRecording) Publish(snapshot);
                        return snapshot.Result is not null;
                    }).ConfigureAwait(false);
                    hadResult |= hasResult;
                }
                catch (Exception e)
                {
                    // A failed live update only skips one refresh; the saved analysis is unaffected.
                    _log.Log(AppLogLevel.Warning, "recording", e, "a live update failed");
                    if (_crashes.IsEnabled) _crashes.Report(e, "recording.live");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Analysis thread (or, for the final snapshot, a pool thread): the handlers do their work here.
    private void Publish(LiveSnapshot snapshot)
    {
        Latest = snapshot;
        SnapshotReady?.Invoke(snapshot);
    }

    /// <summary>
    /// Stops recording and saves the take: the final live update on the trimmed audio, the full
    /// analysis of the file, then the take is stored (UI thread). The live views stay on the final
    /// state until <see cref="FinishAsync"/>.
    /// </summary>
    /// <param name="progress">How far it has got, 0–1 (the last live update, the analysis, saving).</param>
    public async Task<Recording> StopAsync(string label, IProgress<double> progress)
    {
        // Shares of the time after Stop, measured on a 27.6 s take: the last live update, the analysis, saving.
        const double liveShare = 0.15, analysisShare = 0.80;
        var stopStart = Stopwatch.GetTimestamp();
        IsRecording = false;
        _loopStop?.Cancel();
        string? wav = null;
        try
        {
            wav = await _recorder.StopAsync(); // after this no more samples arrive
            _recorder.SamplesCaptured -= OnSamplesCaptured;
            await _loop; // an update still running finishes first

            // Trim the take so its pitch frames sit exactly on the live ones, then freeze the
            // live view on the final live state while the saved analysis runs.
            if (_live is { } live)
            {
                var recorded = (int)((new FileInfo(wav).Length - 44) / 2);
                var length = live.AlignedLength(Math.Min(recorded, live.SampleCount));
                WavWriter.Truncate(wav, length);
                var finalStart = Stopwatch.GetTimestamp();
                await _scheduler.RunSavedAsync(() =>
                {
                    var snapshot = live.Update(final: true, length: length);
                    Publish(snapshot);
                    return snapshot;
                });
                Perf.Report("stop.final-live", Stopwatch.GetElapsedTime(finalStart).TotalMilliseconds);
            }
            progress.Report(liveShare);

            var analyzeStart = Stopwatch.GetTimestamp();
            var result = await _engine.AnalyzeAsync(wav, progress: new Progress(f => progress.Report(liveShare + analysisShare * f)));
            progress.Report(liveShare + analysisShare);
            var analysisMs = Stopwatch.GetElapsedTime(analyzeStart).TotalMilliseconds;
            Perf.Report("stop.analyze", analysisMs);
            var entry = result.Metrics;
            entry.Label = string.IsNullOrWhiteSpace(label) ? "untitled take" : label.Trim();
            entry.Note = "";
            entry.Date = DateTime.Now.ToString("yyyy-MM-dd");
            entry.RecordedAt = Recording.Timestamp(DateTime.Now);
            entry.SourceFile = Path.GetFileName(wav);
            var sourceWav = wav;
            Recording saved;
            using (Perf.Measure("stop.save")) saved = await Task.Run(() => _store.Add(entry, result.Detail, sourceWav, result.Series));
            progress.Report(1);
            _savedId = saved.Id;
            var stopSeconds = Stopwatch.GetElapsedTime(stopStart).TotalSeconds;
            _log.Log(AppLogLevel.Info, "recording", $"saved a {saved.DurationS ?? 0:0.0} s take: analysis {analysisMs:0} ms, Stop to saved {stopSeconds:0.00} s");
            if (_analytics.IsEnabled) _analytics.TakeAnalyzed(new(TakeSource.Recorded, saved, stopSeconds));
            return saved;
        }
        catch (Exception e)
        {
            _log.Log(AppLogLevel.Error, "recording", e, "Stop failed");
            if (_crashes.IsEnabled) _crashes.Report(e, "recording.stop");
            throw;
        }
        finally
        {
            if (wav is not null) try { File.Delete(wav); } catch (IOException) { }
        }
    }

    /// <summary>After a saved take: shows it, announces it, and ends the live views (UI thread).</summary>
    public async Task FinishAsync()
    {
        _active.SelectLatest();
        await _catalog.ReloadAsync();
        if (_savedId is { } id) _messenger.Send(new TakeSaved(id));
        _savedId = null;
        End();
    }

    /// <summary>Abandons the recording after a failure (UI thread): the microphone is released and the live views end.</summary>
    public void Cancel()
    {
        IsRecording = false;
        _loopStop?.Cancel();
        _recorder.SamplesCaptured -= OnSamplesCaptured;
        _recorder.Cancel();
        End();
    }

    private void End()
    {
        _live = null;
        Latest = null;
        if (!IsLive) return;
        IsLive = false;
        Ended?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Progress forwarded as it comes (on the analysis' threads), without capturing a context.</summary>
    private sealed class Progress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
