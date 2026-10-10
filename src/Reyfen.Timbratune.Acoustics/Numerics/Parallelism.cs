namespace Reyfen.Timbratune.Acoustics.Numerics;

/// <summary>
/// How many cores the analyses' parallel loops may use, for the code running inside a
/// <see cref="Limit"/> scope (and the loops' own worker tasks, which inherit it). Results
/// never depend on it — only how much of the machine the work takes: the live analysis
/// runs within a limit so the UI and the renderer always have cores left.
/// </summary>
public static class Parallelism
{
    private static readonly AsyncLocal<int> s_limit = new();
    private static readonly ParallelOptions s_unlimited = new();
    private static readonly ParallelOptions?[] s_limited = new ParallelOptions?[65];

    /// <summary>Loops started until the returned scope is disposed use at most <paramref name="maxDegree"/> cores (≤ 0 = no limit).</summary>
    public static Scope Limit(int maxDegree)
    {
        var previous = s_limit.Value;
        s_limit.Value = Math.Max(0, maxDegree);
        return new Scope(previous);
    }

    /// <summary>The options the loops pass to <see cref="Parallel"/>.</summary>
    public static ParallelOptions Options
    {
        get
        {
            var limit = s_limit.Value;
            if (limit <= 0) return s_unlimited;
            if (limit >= s_limited.Length) return new ParallelOptions { MaxDegreeOfParallelism = limit };
            return s_limited[limit] ??= new ParallelOptions { MaxDegreeOfParallelism = limit };
        }
    }

    public readonly struct Scope(int previous) : IDisposable
    {
        public void Dispose() => s_limit.Value = previous;
    }
}
