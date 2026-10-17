namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// Where the analyses run. The live analysis must never compete with the UI and the renderer
/// for every core (it made a Pixel 9 drop frames, HANDOFF §8d); the saved analysis after Stop
/// may use them all. A browser head would run both in a Web Worker instead (REFACTOR-PLAN §7).
/// </summary>
public interface IAnalysisScheduler
{
    /// <summary>
    /// Cores the live analysis may use: &gt; 0 = on one dedicated, lower-priority thread with its
    /// loops limited to that many cores (updates then start at most every 100 ms); 0 = all cores,
    /// on thread-pool threads at normal priority.
    /// </summary>
    int LiveCores { get; }

    /// <summary>Runs one live update (analysis and smoothing) off the UI thread; updates run one at a time, in order.</summary>
    Task<T> RunLiveAsync<T>(Func<T> work);

    /// <summary>Runs a saved analysis (after Stop, an import, a backfill) off the UI thread, on every core.</summary>
    Task<T> RunSavedAsync<T>(Func<T> work, CancellationToken cancellationToken = default);
}

/// <summary>
/// The native heads' scheduler: live updates on a <see cref="LiveWorker"/> (created on first
/// use, kept for the process), saved analyses on the thread pool. This is exactly what the app
/// did before the scheduler existed.
/// </summary>
public sealed class NativeAnalysisScheduler(int liveCores, Action? lowerThreadPriority) : IAnalysisScheduler, IDisposable
{
    /// <summary>The live analysis' cores on the native heads (measured on a Pixel 9, HANDOFF §8d).</summary>
    public const int DefaultLiveCores = 2;

    /// <summary>The thread pool only: what tests, tools and the CLI use.</summary>
    public static NativeAnalysisScheduler Default { get; } = new(0, null);

    private readonly object _gate = new();
    private LiveWorker? _worker;

    public int LiveCores { get; } = Math.Max(0, liveCores);

    public Task<T> RunLiveAsync<T>(Func<T> work)
    {
        if (LiveCores == 0) return Task.Run(work);
        LiveWorker worker;
        lock (_gate) worker = _worker ??= new LiveWorker(LiveCores, lowerThreadPriority);
        return worker.Run(work);
    }

    public Task<T> RunSavedAsync<T>(Func<T> work, CancellationToken cancellationToken = default) => Task.Run(work, cancellationToken);

    public void Dispose()
    {
        lock (_gate) _worker?.Dispose();
    }
}
