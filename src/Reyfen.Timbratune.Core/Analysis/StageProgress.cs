namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// Folds the progress of several analysis stages (parallel or in sequence) into
/// one fraction, each stage weighted by its measured share of the work. The
/// reported value only ever grows. Thread-safe.
/// </summary>
internal sealed class StageProgress(Action<double>? report, params double[] weights)
{
    private readonly double[] _done = new double[weights.Length];
    private readonly double _total = weights.Sum();
    private readonly Lock _gate = new();
    private double _last;

    /// <summary>The callback for stage <paramref name="index"/>: its own fraction done (0–1).</summary>
    public Action<double>? Stage(int index) => report is null ? null : fraction => Set(index, fraction);

    public void Complete(int index) => Set(index, 1);

    private void Set(int index, double fraction)
    {
        if (report is null) return;
        // Reported under the lock so values arrive in order (the callback must be quick).
        lock (_gate)
        {
            _done[index] = Math.Max(_done[index], Math.Clamp(fraction, 0, 1));
            var sum = 0.0;
            for (var i = 0; i < _done.Length; i++) sum += weights[i] * _done[i];
            var value = sum / _total;
            if (value <= _last) return;
            _last = value;
            report(value);
        }
    }
}
