using System.Collections.Concurrent;
using Reyfen.Timbratune.Acoustics.Numerics;

namespace Reyfen.Timbratune.Analysis;

/// <summary>
/// One long-lived thread for the live analysis, optionally at a lower OS priority (set by the
/// platform through <paramref name="lowerPriority"/>, run once on that thread) and with its
/// parallel loops limited to <paramref name="maxDegree"/> cores, so the UI and the renderer
/// always get CPU time while recording. Work items run one at a time, in order.
/// </summary>
public sealed class LiveWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public LiveWorker(int maxDegree, Action? lowerPriority)
    {
        _thread = new Thread(() =>
        {
            try { lowerPriority?.Invoke(); }
            catch (Exception) { /* a priority we can't set just leaves the default */ }
            using var limit = Parallelism.Limit(maxDegree);
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        }) { IsBackground = true, Name = "Live analysis" };
        _thread.Start();
    }

    public Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    public void Dispose() => _queue.CompleteAdding();
}
