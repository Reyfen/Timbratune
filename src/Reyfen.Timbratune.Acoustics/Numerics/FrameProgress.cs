namespace Reyfen.Timbratune.Acoustics.Numerics;

/// <summary>
/// Counts finished frames of a parallel loop and reports the fraction done
/// about 100 times over the loop (not on every frame), from whichever thread
/// finishes the frame. Does nothing without a callback.
/// </summary>
internal sealed class FrameProgress(int total, Action<double>? report)
{
    private readonly int _stride = Math.Max(1, total / 100);
    private int _done;

    public void Done()
    {
        if (report is null) return;
        var done = Interlocked.Increment(ref _done);
        if (done % _stride == 0) report((double)done / total);
    }
}
