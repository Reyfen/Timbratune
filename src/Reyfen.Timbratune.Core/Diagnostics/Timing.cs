using System.Diagnostics;

namespace Reyfen.Timbratune.Core.Diagnostics;

/// <summary>
/// Step timings inside the analysis, for profiling builds: off unless <see cref="Sink"/> is
/// set (the UI's Perf probes connect it), then each measured step reports (name, ms), and
/// the memory it allocated on its thread as ("alloc." + name, KB).
/// </summary>
public static class Timing
{
    public static Action<string, double>? Sink { get; set; }

    public static Scope Measure(string name) =>
        Sink is null ? default : new(name, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    public readonly struct Scope(string name, long start, long allocated) : IDisposable
    {
        public void Dispose()
        {
            if (start == 0 || Sink is not { } sink) return;
            sink(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            sink("alloc." + name, (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0);
        }
    }
}
