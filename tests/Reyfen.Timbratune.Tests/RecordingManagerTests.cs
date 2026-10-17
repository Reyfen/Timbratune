using CommunityToolkit.Mvvm.Messaging;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Features.Record;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Tests;

/// <summary>
/// The whole recording flow without a UI: a fixture replayed in real time as the microphone,
/// the live session's updates, Stop, the saved take. About 10 s.
/// </summary>
public sealed class RecordingManagerTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-recording-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Reyfen.Timbratune.Core.Tests", "Fixtures", name + ".wav"));

    [Fact]
    public async Task RecordingFollowsTheMicrophoneLiveAndSavesTheTake()
    {
        var store = new RecordingStore(new DataPaths(_root));
        var catalog = new TakeCatalog(store);
        var active = new ActiveTakeService(catalog);
        var messenger = new WeakReferenceMessenger();
        using var recorder = new FileReplayRecorder(Fixture("vctk_f294"));
        using var scheduler = new NativeAnalysisScheduler(NativeAnalysisScheduler.DefaultLiveCores, null);
        var manager = new RecordingManager(recorder, new AcousticsAnalysisEngine(scheduler), store, scheduler,
            new NoRuntimePermissions(), catalog, active, messenger);

        int snapshots = 0, results = 0, offWorker = 0, started = 0, ended = 0, recordingStarted = 0;
        LiveSnapshot? final = null;
        TakeSaved? saved = null;
        manager.Started += (_, _) => started++;
        manager.Ended += (_, _) => ended++;
        manager.SnapshotReady += s =>
        {
            Interlocked.Increment(ref snapshots);
            if (s.Result is not null) Interlocked.Increment(ref results);
            // Live ticks on the scheduler's one live thread; the final snapshot on a pool thread.
            var thread = Thread.CurrentThread;
            if (s.IsFinal ? !thread.IsThreadPoolThread : thread.Name != "Live analysis") Interlocked.Increment(ref offWorker);
            if (s.IsFinal) final = s;
        };
        messenger.Register<RecordingManagerTests, TakeSaved>(this, (_, m) => saved = m);
        messenger.Register<RecordingManagerTests, RecordingStarted>(this, (_, _) => recordingStarted++);

        var recording = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(await manager.StartAsync());
        Assert.True(manager.IsLive && manager.IsRecording);
        const double seconds = 9.81; // vctk_f294
        await Task.Delay(TimeSpan.FromSeconds(seconds + 0.4));
        var progress = new List<double>();
        var stopping = manager.StopAsync("  a test take  ", new SyncProgress(progress.Add));
        var recorded = recording.Elapsed.TotalSeconds; // the live loop ticks until Stop, past the end of the clip
        var take = await stopping;
        Assert.True(manager.IsLive); // frozen on the final snapshot until the take is shown
        Assert.False(manager.IsRecording);
        await manager.FinishAsync();

        output.WriteLine($"{snapshots} snapshots ({results} with a result) for {seconds} s; {progress.Count} progress reports");
        Assert.Equal(1, started);
        Assert.Equal(1, ended);
        Assert.Equal(1, recordingStarted);
        Assert.False(manager.IsLive);
        // Never more than one update per 100 ms tick of the whole recording (plus the final one).
        // Fewer when the machine is busy (e.g. the other test assemblies running in parallel): a slow
        // update makes the next one wait for the next tick, by design. Alone it is about 100–103 for
        // the ~10.2 s recorded; a Debug build measuring five formant ceilings on one core while the
        // other assemblies run gets far fewer, so the floor is one per 2 s: what matters is that they keep coming.
        Assert.InRange(snapshots, (int)(seconds / 2), (int)(recorded * 10) + 2);
        // Full updates (with the whole take's statistics) at most about once a second, and the final one.
        Assert.InRange(results, 3, (int)seconds + 3);
        Assert.Equal(0, offWorker); // raised on the analysis threads

        Assert.Equal("a test take", take.Label);
        Assert.Equal(take.Id, saved?.TakeId);
        Assert.Equal(take.Id, Assert.Single(catalog.Takes).Id);
        Assert.Equal(take.Id, active.Active?.Id);
        Assert.Equal(1, progress[^1]);
        Assert.True(progress.Zip(progress.Skip(1)).All(p => p.Second >= p.First - 1e-12), "progress never goes back");

        // Live and saved agree: the final live contour is the saved take's, frame for frame.
        Assert.NotNull(final?.Result);
        var detail = store.LoadDetail(take)!;
        Assert.Equal(detail.Frames.T, final!.Result!.Detail.Frames.T);
        Assert.Equal(detail.Frames.Hz, final.Result.Detail.Frames.Hz);
        Assert.Equal(take.Pitch.MeanHz, final.Result.Metrics.Pitch.MeanHz);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath(), "timbratune-take-*.wav"), f => File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddSeconds(-30));
    }

    [Fact]
    public async Task WithoutTheMicrophoneNothingStarts()
    {
        var store = new RecordingStore(new DataPaths(_root));
        var catalog = new TakeCatalog(store);
        using var recorder = new FileReplayRecorder(Fixture("vctk_f294"));
        var manager = new RecordingManager(recorder, new AcousticsAnalysisEngine(), store, NativeAnalysisScheduler.Default,
            new Refused(), catalog, new ActiveTakeService(catalog), new WeakReferenceMessenger());
        var started = false;
        manager.Started += (_, _) => started = true;
        Assert.False(await manager.StartAsync());
        Assert.False(started || manager.IsLive || recorder.IsRecording);
    }

    private sealed class Refused : IPermissions
    {
        public Task<bool> RequestMicrophoneAsync() => Task.FromResult(false);
    }

    /// <summary>Reports straight away, on the reporting thread.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        private readonly object _gate = new();
        public void Report(double value)
        {
            lock (_gate) report(value);
        }
    }
}
