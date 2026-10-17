// The formant ceiling path through this repo's engine, for the research benches (RESONANCE-PLAN.md F5;
// the scoring scripts are in the research clone, research/py).
//
//   formant-check -dump <dir> [-method Path|Legacy] [-jobs N] (-list files.txt | file.wav …)
//       the saved analysis of each file into <dir>/<name>.txt, in the research tool's dump format:
//         m <F1> <F2> <F3> <weight>      the take's cards (Hz, Hz, Hz, corrected H1*-A3* dB)
//         c <start> <ceiling>            the ceiling path's segments (start 0 for the first)
//         s <t> <F1> <F2> <F3>           the formants the graphs read, every 6.25 ms
//         p <t> <F1> <F2> <F3> <F4>      the same with F4 (for perceived size)
//         z <ΔF> <VTL> <median F0>       perceived size (Hz, cm) and the median pitch (Hz)
//         r <t> <F1> <F2> <F3> <F4>      the vowel-core rows
//         w <t> <F0> <weight>            each weight frame
//   formant-check -live [-method …] (-list … | file.wav …)
//       each file recorded live as the app does (irregular chunks, an update every 100 ms, the take
//       trimmed on Stop) and compared with the saved analysis of the trimmed audio: the cards, the
//       path, and the time of each analysis.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Analysis.Metrics.Weight;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Acoustics.Streaming;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string? dumpDir = null;
var live = false;
var method = FormantCeilingMethod.Path;
var jobs = Math.Max(1, Environment.ProcessorCount / 3);
var files = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-dump": dumpDir = args[++i]; break;
        case "-live": live = true; break;
        case "-method": method = Enum.Parse<FormantCeilingMethod>(args[++i], ignoreCase: true); break;
        case "-jobs": jobs = int.Parse(args[++i]); break;
        case "-list": files.AddRange(File.ReadAllLines(args[++i]).Select(l => l.Trim()).Where(l => l.Length > 0)); break;
        default: files.Add(args[i]); break;
    }
}
if (files.Count == 0 || (dumpDir is null && !live))
{
    Console.Error.WriteLine("usage: formant-check (-dump <dir> | -live) [-method Path|Legacy] [-jobs N] (-list files.txt | file.wav …)");
    return 1;
}

static Sound Read(string file)
{
    using var s = File.OpenRead(file);
    return WavDecoder.Decode(s);
}

if (dumpDir is not null)
{
    Directory.CreateDirectory(dumpDir);
    var done = 0;
    var sw = Stopwatch.StartNew();
    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = jobs }, file =>
    {
        var outFile = Path.Combine(dumpDir, Path.GetFileNameWithoutExtension(file) + ".txt");
        if (File.Exists(outFile)) return;
        var sound = Read(file);
        var tracks = AcousticsAnalysisEngine.MeasureTracks(sound, RawAnalysisAssembler.LoudOutlierFraction, null, method);
        var raw = RawAnalysisAssembler.Assemble(tracks);
        var metrics = AnalysisPostProcessor.Process(raw).Metrics;
        var track = tracks.Formants;
        var sb = new StringBuilder();
        sb.Append($"m {metrics.Formants.F1Hz:0.#} {metrics.Formants.F2Hz:0.#} {metrics.Formants.F3Hz:0.#} {metrics.Weight?.H1a3cDb:0.##}\n");
        if (tracks.CeilingPath is { } path)
            for (var k = 0; k < path.SegmentCount; k++)
            {
                var start = path.SegmentStart(k);
                sb.Append($"c {(double.IsFinite(start) ? start : 0):0.#####} {tracks.FormantCeilings[path.SegmentCeiling(k)]:0}\n");
            }
        for (var t = 0.025; t < sound.Duration - 0.025; t += 0.00625)
            sb.Append($"s {t:0.#####} {track.ValueAtTime(1, t):0.#} {track.ValueAtTime(2, t):0.#} {track.ValueAtTime(3, t):0.#}\n");
        for (var t = 0.025; t < sound.Duration - 0.025; t += 0.00625)
            sb.Append($"p {t:0.#####} {track.ValueAtTime(1, t):0.#} {track.ValueAtTime(2, t):0.#} {track.ValueAtTime(3, t):0.#} {track.ValueAtTime(4, t):0.#}\n");
        // Perceived size (RESONANCE-PLAN F8): the take's ΔF (Hz), VTL (cm) and median F0 (Hz), and the vowel-core rows.
        sb.Append($"z {metrics.Formants.DeltaFHz:0.##} {metrics.Formants.VtlCm:0.##} {metrics.Pitch.MedianHz:0.##}\n");
        foreach (var r in raw.Formants)
            sb.Append($"r {r.T:0.#####} {r.F1:0.#} {r.F2:0.#} {r.F3:0.#} {r.F4:0.#}\n");
        var fs = raw.Get("sampling_frequency");
        foreach (var w in raw.WeightFrames)
            sb.Append($"w {w.T:0.#####} {w.F0:0.#} {WeightModule.CorrectedH1A3(w, fs):0.##}\n");
        File.WriteAllText(outFile, sb.ToString());
        var n = Interlocked.Increment(ref done);
        if (n % 50 == 0) Console.Error.WriteLine($"{n} / {files.Count} ({sw.Elapsed.TotalSeconds:0} s)");
    });
    Console.WriteLine($"dumped {files.Count} files into {dumpDir} in {sw.Elapsed.TotalSeconds:0.0} s");
    return 0;
}

// Live vs saved.
var rows = new ConcurrentBag<(string Name, double[] Saved, double[] Live, double SavedCeiling, double LiveCeiling, bool SamePath, double SavedMs, double LiveMs, double P95Ms)>();
Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = jobs }, file =>
{
    var samples = Read(file).ToMono();
    var fsRate = Read(file).SamplingFrequency;
    var analyzer = new LiveAnalyzer(fsRate, formantMethod: method);
    var provider = new LiveTrackProvider(fsRate, formantMethod: method);
    var signal = new LiveSignal(fsRate);
    var rng = new Random(11);
    var step = (int)(0.1 * fsRate);
    var next = step;
    var ticks = new List<double>();
    var liveSw = new Stopwatch();
    for (var i = 0; i < samples.Length;)
    {
        var n = Math.Min(rng.Next(200, 1500), samples.Length - i);
        analyzer.Append(samples.AsSpan(i, n));
        signal.Append(samples.AsSpan(i, n));
        i += n;
        if (i < next) continue;
        var t0 = Stopwatch.GetTimestamp();
        liveSw.Start();
        analyzer.Update();
        liveSw.Stop();
        ticks.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        provider.Update(signal.View(), TrackSet.Live, final: false);
        next += step;
    }
    var length = analyzer.AlignedLength(analyzer.SampleCount);
    liveSw.Start();
    var final = analyzer.Update(final: true, length: length);
    liveSw.Stop();
    var livePath = provider.Update(signal.View(length), TrackSet.All, final: true)!.Value.CeilingPath;

    var trimmed = new Sound(samples[..length], fsRate);
    var savedSw = Stopwatch.StartNew();
    var savedTracks = AcousticsAnalysisEngine.MeasureTracks(trimmed, RawAnalysisAssembler.LoudOutlierFraction, null, method);
    var saved = AnalysisPostProcessor.Process(RawAnalysisAssembler.Assemble(savedTracks)).Metrics;
    savedSw.Stop();
    var a = final.Result!.Metrics;
    double[] Cards(Reyfen.Timbratune.Core.Models.Recording r) =>
        [r.Formants.F1Hz ?? double.NaN, r.Formants.F2Hz ?? double.NaN, r.Formants.F3Hz ?? double.NaN, r.Weight?.H1a3cDb ?? double.NaN];
    var samePath = livePath is null || savedTracks.CeilingPath is null
        ? livePath is null && savedTracks.CeilingPath is null
        : SamePath(livePath, savedTracks.CeilingPath);
    ticks.Sort();
    rows.Add((Path.GetFileNameWithoutExtension(file), Cards(saved), Cards(a), saved.Formants.CeilingHz ?? double.NaN,
        a.Formants.CeilingHz ?? double.NaN, samePath, savedSw.Elapsed.TotalMilliseconds, liveSw.Elapsed.TotalMilliseconds,
        ticks.Count > 0 ? ticks[(int)Math.Ceiling(0.95 * ticks.Count) - 1] : 0));
});

static bool SamePath(CeilingPath a, CeilingPath b)
{
    if (a.SegmentCount != b.SegmentCount) return false;
    for (var k = 0; k < a.SegmentCount; k++)
        if (a.SegmentCeiling(k) != b.SegmentCeiling(k) || Math.Abs(a.SegmentStart(k) - b.SegmentStart(k)) > 0.1 && double.IsFinite(a.SegmentStart(k)))
            return false;
    return true;
}

static double Quantile(List<double> xs, double q)
{
    var v = xs.Where(double.IsFinite).OrderBy(x => x).ToList();
    return v.Count == 0 ? double.NaN : v[Math.Min(v.Count - 1, (int)Math.Floor(q * v.Count))];
}

foreach (var r in rows.OrderBy(r => r.Name, StringComparer.Ordinal))
    Console.WriteLine($"{r.Name,-40} ceiling saved {r.SavedCeiling,5:0} live {r.LiveCeiling,5:0} | path {(r.SamePath ? "same" : "DIFFERS")} | " +
                      $"F1-F3 saved {r.Saved[0],5:0} {r.Saved[1],5:0} {r.Saved[2],5:0} live {r.Live[0],5:0} {r.Live[1],5:0} {r.Live[2],5:0} | " +
                      $"weight {r.Saved[3],6:0.00} {r.Live[3],6:0.00} | saved {r.SavedMs,5:0} ms, live {r.LiveMs,5:0} ms, p95 {r.P95Ms,5:0.0} ms");
var all = rows.ToList();
Console.WriteLine();
Console.WriteLine($"{all.Count} files; same main ceiling {all.Count(r => r.SavedCeiling.Equals(r.LiveCeiling))}, same path {all.Count(r => r.SamePath)}");
string[] names = ["F1", "F2", "F3", "weight"];
for (var k = 0; k < 4; k++)
{
    var d = all.Select(r => Math.Abs(r.Saved[k] - r.Live[k])).ToList();
    Console.WriteLine($"  |Δ{names[k]}|: median {Quantile(d, 0.5):0.##}, p90 {Quantile(d, 0.9):0.##}, max {Quantile(d, 1):0.##}");
}
return 0;
