using Avalonia.Threading;

namespace Reyfen.Timbratune.Shared;

/// <summary>
/// Progress that reaches the UI thread at most every half percent: the analysis
/// reports hundreds of times a second, from worker threads.
/// </summary>
public sealed class ThrottledProgress : IProgress<double>
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
