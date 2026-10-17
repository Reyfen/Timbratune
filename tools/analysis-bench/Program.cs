// Times the analyses and counts what they allocate, so a refactoring can show it costs
// nothing (REFACTOR-PLAN.md §3, gate 2: at most +3 % time, no more allocations).
//   full         AcousticsAnalysisEngine.AnalyzeAsync on the take (decode, measure, assemble,
//                post-process, series), all cores, as after Stop
//   live         the take replayed through LiveAnalyzer as the app records it: 10 ms chunks, an
//                update every 100 ms (a full one every 10th, quick ones between), on one thread
//                with its loops limited to 2 cores (LiveWorker), then the final update
//   live.compute LiveTimelinesViewModel.Compute on each snapshot (the smoothing, same thread)
// Allocations are GC.GetTotalAllocatedBytes(precise) around the measured work: the loops run on
// pool threads too, which a per-thread count would miss. Results are hashed (SHA-256 of the
// JSON the store would write) so a run on other code also shows whether the numbers moved.
// Without a WAV it builds the ~27.6 s take of scripts/make-long-wav.ps1 from the fixtures.
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Metrics.Clarity;
using Reyfen.Timbratune.Core.Analysis.Metrics.Loudness;
using Reyfen.Timbratune.Core.Analysis.Metrics.Resonance;
using Reyfen.Timbratune.Core.Analysis.Metrics.Steadiness;
using Reyfen.Timbratune.Core.Analysis.Metrics.Weight;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Features.LiveMonitor;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string? wavArg = null, outPath = null, baselinePath = null;
var runs = 10;
var types = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-runs": runs = int.Parse(args[++i]); break;
        case "-out": outPath = args[++i]; break;
        case "-baseline": baselinePath = args[++i]; break;
        case "-types": types = true; break;
        default: wavArg = args[i]; break;
    }
}
#if DEBUG
Console.Error.WriteLine("warning: Debug build; measure with -c Release");
#endif

var wav = wavArg ?? LongWav();
double[] mono;
double fs;
using (var f = File.OpenRead(wav))
{
    var sound = WavDecoder.Decode(f);
    mono = sound.ToMono();
    fs = sound.SamplingFrequency;
}
var seconds = mono.Length / fs;
Console.WriteLine($"take: {(wavArg ?? "long.wav (built from the fixtures)")}, {seconds:0.0} s; {runs} runs + 1 warm-up; " +
                  $"{Environment.ProcessorCount} cores, {RuntimeInformationText()}");

var full = new List<double>();
var fullAlloc = new List<double>();
var lightTicks = new List<double>();
var fullTicks = new List<double>();
var liveTotal = new List<double>();
var liveAlloc = new List<double>();
var computeTotal = new List<double>();
var computeAlloc = new List<double>();
string? savedHash = null, liveHash = null;

for (var run = 0; run <= runs; run++)
{
    var warmUp = run == 0;

    // Full analysis.
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var a0 = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();
    var result = await new AcousticsAnalysisEngine().AnalyzeAsync(wav);
    sw.Stop();
    var a1 = GC.GetTotalAllocatedBytes(true);
    savedHash ??= Hash(TimbratuneJson.WriteRecordings([result.Metrics]), TimbratuneJson.WriteDetail(result.Detail), SeriesJson(result));

    // Live, on its own thread like LiveWorker.
    var live = RunLive(mono, fs);
    if (warmUp) continue;
    full.Add(sw.Elapsed.TotalMilliseconds);
    fullAlloc.Add((a1 - a0) / 1e6);
    lightTicks.AddRange(live.Light);
    fullTicks.AddRange(live.Full);
    liveTotal.Add(live.UpdateMs);
    liveAlloc.Add(live.UpdateBytes / 1e6 / seconds);
    computeTotal.Add(live.ComputeMs);
    computeAlloc.Add(live.ComputeBytes / 1e6 / seconds);
    liveHash ??= live.Hash;
}

var report = new JsonObject
{
    ["take_s"] = Math.Round(seconds, 2),
    ["runs"] = runs,
    ["machine"] = $"{Environment.MachineName}, {Environment.ProcessorCount} cores",
    ["full_ms"] = Median(full),
    ["full_alloc_mb"] = Median(fullAlloc),
    ["live_update_light_ms"] = Median(lightTicks),
    ["live_update_full_ms"] = Median(fullTicks),
    ["live_update_total_ms"] = Median(liveTotal),
    ["live_alloc_mb_per_s"] = Median(liveAlloc),
    ["live_compute_total_ms"] = Median(computeTotal),
    ["live_compute_alloc_mb_per_s"] = Median(computeAlloc),
    ["saved_hash"] = savedHash,
    ["live_hash"] = liveHash,
};

Console.WriteLine();
Console.WriteLine("measure                          median      (min – max)");
Row("full analysis (ms)", full);
Row("full analysis alloc (MB)", fullAlloc);
Row("live update, quick (ms/tick)", lightTicks);
Row("live update, full (ms/tick)", fullTicks);
Row("live updates, whole take (ms)", liveTotal);
Row("live alloc (MB/s of audio)", liveAlloc);
Row("live.compute, whole take (ms)", computeTotal);
Row("live.compute alloc (MB/s)", computeAlloc);
Console.WriteLine($"saved results hash {savedHash}");
Console.WriteLine($"live results hash  {liveHash}");

if (types) AllocationsByType(mono, fs);

if (outPath is not null)
{
    File.WriteAllText(outPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {outPath}");
}

var exit = 0;
if (baselinePath is not null)
{
    var baseline = JsonNode.Parse(File.ReadAllText(baselinePath))!.AsObject();
    Console.WriteLine();
    Console.WriteLine($"against {baselinePath} (gate: time ≤ +3 %, allocations ≤ baseline + measurement noise):");
    // Allowed growth. Allocations aren't exactly repeatable: ArrayPool.Shared keeps per-thread
    // caches, so what a run allocates depends on which pool threads the parallel loops land on.
    // Measured spread of the medians between sessions (12 runs each): live < 0.1 %, full ~4 %.
    foreach (var (key, allowed) in new[]
             {
                 ("full_ms", 0.03), ("full_alloc_mb", 0.05), ("live_update_light_ms", 0.03), ("live_update_full_ms", 0.03),
                 ("live_update_total_ms", 0.03), ("live_alloc_mb_per_s", 0.01), ("live_compute_total_ms", 0.03),
                 ("live_compute_alloc_mb_per_s", 0.01),
             })
    {
        if (baseline[key] is not { } b) continue;
        var before = b.GetValue<double>();
        var now = report[key]!.GetValue<double>();
        var change = before == 0 ? 0 : (now - before) / before;
        var ok = change <= allowed;
        if (!ok) exit = 1;
        Console.WriteLine($"  {(ok ? "  " : "!!")} {key,-28} {before,10:0.###} → {now,10:0.###}  {Percent(change),7}");
    }
    foreach (var key in new[] { "saved_hash", "live_hash" })
    {
        var same = baseline[key]?.GetValue<string>() == report[key]?.GetValue<string>();
        if (!same && key == "saved_hash") exit = 1;
        Console.WriteLine($"  {(same ? "  " : key == "saved_hash" ? "!!" : " ~")} {key,-28} {(same ? "identical" : "DIFFERENT")}" +
                          (same || key == "saved_hash" ? "" : " (live may move ≤ 1e-7: check the golden tests)"));
    }
}
if (wavArg is null) File.Delete(wav);
return exit;

static LiveRun RunLive(double[] mono, double fs)
{
    LiveRun? run = null;
    var thread = new Thread(() =>
    {
        using var limit = Parallelism.Limit(2); // as LiveWorker in the app
        var window = LiveWindowOption.All[0].Seconds;
        var lines = LiveLine.From(MetricRegistry.Default);
        var live = new LiveAnalyzer(fs);
        var chunk = (int)Math.Round(fs / 100);
        var tick = (int)Math.Round(fs / 10);
        var light = new List<double>();
        var fullTicks = new List<double>();
        double updateMs = 0, computeMs = 0;
        long updateBytes = 0, computeBytes = 0;
        var n = 0;
        LiveSnapshot? last = null;
        for (int i = 0, next = tick; i < mono.Length;)
        {
            var count = Math.Min(chunk, mono.Length - i);
            live.Append(mono.AsSpan(i, count));
            i += count;
            if (i < next) continue;
            next += tick;
            var isFull = n++ % 10 == 0;
            var (snapshot, ms, bytes) = Measure(() => live.Update(full: isFull));
            (isFull ? fullTicks : light).Add(ms);
            updateMs += ms;
            updateBytes += bytes;
            var (_, cms, cbytes) = Measure(() => LiveTimelinesViewModel.Compute(snapshot, lines, window));
            computeMs += cms;
            computeBytes += cbytes;
        }
        var length = live.AlignedLength(live.SampleCount);
        var (final, fms, fbytes) = Measure(() => live.Update(final: true, length: length));
        updateMs += fms;
        updateBytes += fbytes;
        last = final;
        run = new LiveRun(light, fullTicks, updateMs, updateBytes, computeMs, computeBytes, LiveHash(last));
    }) { Name = "Live analysis" };
    thread.Start();
    thread.Join();
    return run!;
}

static string Percent(double change)
{
    var pct = Math.Round(100 * change, 1);
    return (pct == 0 ? 0 : pct).ToString("+0.0;-0.0;0.0") + " %";
}

static (T Value, double Ms, long Bytes) Measure<T>(Func<T> work)
{
    var a0 = GC.GetTotalAllocatedBytes(true);
    var t0 = Stopwatch.GetTimestamp();
    var value = work();
    var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    return (value, ms, GC.GetTotalAllocatedBytes(true) - a0);
}

static string SeriesJson(AnalysisResult result)
{
    using var s = new MemoryStream();
    TimbratuneJson.WriteSeries(s, result.Series!);
    return Encoding.UTF8.GetString(s.ToArray());
}

static string LiveHash(LiveSnapshot s)
{
    var b = new StringBuilder();
    // The same lists in the same order as before the registry (P7), so hashes compare across builds.
    foreach (var list in new[] { s.Series[LoudnessModule.Loudness], s.Series[ClarityModule.Hnr], s.Series[ResonanceModule.F2],
                 s.Series[ResonanceModule.F3], s.Series[WeightModule.Weight], s.Series[SteadinessModule.Jitter] })
    {
        foreach (var p in list) b.Append(p.T.ToString("R")).Append(',').Append(p.Value.ToString("R")).Append(';');
        b.Append('|');
    }
    return Hash(TimbratuneJson.WriteRecordings([s.Result!.Metrics]), TimbratuneJson.WriteDetail(s.Result.Detail), b.ToString());
}

static string Hash(params string[] parts) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u0000", parts))))[..16].ToLowerInvariant();

static double Median(List<double> xs)
{
    if (xs.Count == 0) return 0;
    var s = xs.Order().ToArray();
    var m = s.Length % 2 == 1 ? s[s.Length / 2] : 0.5 * (s[s.Length / 2 - 1] + s[s.Length / 2]);
    return Math.Round(m, 3);
}

static void Row(string name, List<double> xs) =>
    Console.WriteLine($"{name,-30} {Median(xs),10:0.###}  ({(xs.Count == 0 ? 0 : xs.Min()):0.###} – {(xs.Count == 0 ? 0 : xs.Max()):0.###})");

static string RuntimeInformationText() =>
    $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";

static string LongWav()
{
    // f294 + f339 + f294, as scripts/make-long-wav.ps1 joins them.
    var fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests",
        "Reyfen.Timbratune.Core.Tests", "Fixtures"));
    var path = Path.Combine(Path.GetTempPath(), $"analysis-bench-{Guid.NewGuid():N}.wav");
    using var w = new WavWriter(path, 44100);
    foreach (var name in new[] { "vctk_f294", "vctk_f339", "vctk_f294" })
    {
        using var f = File.OpenRead(Path.Combine(fixtures, name + ".wav"));
        var samples = WavDecoder.Decode(f).Channel(0);
        var floats = new float[samples.Length];
        for (var i = 0; i < floats.Length; i++) floats[i] = (float)samples[i];
        w.Write(floats);
    }
    return path;
}

// Which types the live analysis allocates (sampled: one AllocationTick event per ~100 KB).
static void AllocationsByType(double[] mono, double fs)
{
    using var listener = new AllocationListener();
    RunLive(mono, fs);
    Console.WriteLine();
    Console.WriteLine("live allocations by type (AllocationTick samples, live updates + compute):");
    foreach (var (type, bytes) in listener.ByType.OrderByDescending(kv => kv.Value).Take(15))
        Console.WriteLine($"  {bytes / 1e6,8:0.0} MB  {type}");
}

sealed record LiveRun(List<double> Light, List<double> Full, double UpdateMs, long UpdateBytes, double ComputeMs, long ComputeBytes, string Hash);

sealed class AllocationListener : EventListener
{
    public readonly Dictionary<string, long> ByType = [];

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1); // GC
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName is null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload is null) return;
        var name = e.PayloadNames!.IndexOf("TypeName") is var t and >= 0 ? e.Payload[t] as string ?? "?" : "?";
        var amount = e.PayloadNames.IndexOf("AllocationAmount64") is var a and >= 0 ? Convert.ToInt64(e.Payload[a]) : 100_000;
        lock (ByType) ByType[name] = ByType.GetValueOrDefault(name) + amount;
    }
}
