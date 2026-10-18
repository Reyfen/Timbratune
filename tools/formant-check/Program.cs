// The formant ceiling path through this repo's engine, for the research benches (RESONANCE-PLAN.md F5;
// the scoring scripts are in the research clone, research/py).
//
//   formant-check -dump <dir> [-pieces] [-method Path|Legacy] [scoring options] [-jobs N] (-list files.txt | file.wav …)
//       scoring options (CeilingScoring; HIGH-PITCH-PLAN experiments), applied in order: -scoring Steady|Plateau|Smoothness
//       (the app's, HANDOFF §8w's, or v0.4.0's), then -stability λ, -smoothness w, -reference NearerNeighbour|BothNeighbours|AllOthersMedian,
//       -formants 2-4, -relative, -floor x, -continuity w, -continuity-to 3|4, -continuity-deadzone ‰, -continuity-pause-to 3|4, -continuity-within, -bandwidth-floor Hz
//       the saved analysis of each file into <dir>/<name>.txt, in the research tool's dump format:
//         m <F1> <F2> <F3> <weight>      the take's cards (Hz, Hz, Hz, corrected H1*-A3* dB)
//         c <start> <ceiling>            the ceiling path's segments (start 0 for the first)
//         s <t> <F1> <F2> <F3>           the formants the graphs read, every 6.25 ms
//         p <t> <F1> <F2> <F3> <F4>      the same with F4 (for perceived size)
//         z <ΔF> <VTL> <median F0>       perceived size (Hz, cm) and the median pitch (Hz)
//         r <t> <F1> <F2> <F3> <F4>      the vowel-core rows
//         w <t> <F0> <weight>            each weight frame
//         q <t0> <t1> <ceiling> …        with -pieces: each voiced piece the path scored and the ceiling it chose
//                                        there, then per candidate log W, per candidate the piece's median F1–F4 and
//                                        B1–B4, and per pair of candidates (0-1, 0-2, …) the median |ΔF1| … |ΔF4|
//         R <t> (<F1–F4> <B1–B4>)×k      with -pieces: each vowel-core row on every candidate
//         K r|g <t> (<F1–F5> <B1–B5>)×k  with -cands: each vowel-core row (r) and graph point of the F3 line (g) on every candidate
//   formant-check -live [-method …] [scoring options] (-list … | file.wav …)
//       each file recorded live as the app does (irregular chunks, an update every 100 ms, the take
//       trimmed on Stop) and compared with the saved analysis of the trimmed audio: the cards, the
//       path, and the time of each analysis. Environment, for diagnosis: FC_PATHS=1 prints both paths (and the
//       live frames' path from a fresh finder: a difference there is the finder's, not the frames'); FC_FRAMES=1
//       how many live candidate frames differ from the saved ones (per second for the 5500 Hz candidate, with
//       the frames whose formant count differs); FC_VOICED=1 counts voiced frames only.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Analysis.Metrics.Weight;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Acoustics.Streaming;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string? dumpDir = null;
var live = false;
var pieces = false;
var cands = false;
var method = FormantCeilingMethod.Path;
var jobs = Math.Max(1, Environment.ProcessorCount / 3);
var files = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-dump": dumpDir = args[++i]; break;
        case "-live": live = true; break;
        case "-pieces": pieces = true; break;
        case "-cands": cands = true; break;
        case "-method": method = Enum.Parse<FormantCeilingMethod>(args[++i], ignoreCase: true); break;
        case "-jobs": jobs = int.Parse(args[++i]); break;
        // HIGH-PITCH-PLAN experiments: the ceiling scoring, from the app's (CeilingScoring.Plateau) or from -scoring,
        // changed by the options after it, in order.
        case "-scoring":
            CeilingScoring.Default = args[++i].ToLowerInvariant() switch { "smoothness" => CeilingScoring.Smoothness, "plateau" => CeilingScoring.Plateau, _ => CeilingScoring.Steady };
            break;
        case "-stability": CeilingScoring.Default = CeilingScoring.Default with { StabilityWeight = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-smoothness": CeilingScoring.Default = CeilingScoring.Default with { SmoothnessWeight = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-reference": CeilingScoring.Default = CeilingScoring.Default with { Reference = Enum.Parse<StabilityReference>(args[++i], ignoreCase: true) }; break;
        case "-formants":
            var range = args[++i].Split('-');
            CeilingScoring.Default = CeilingScoring.Default with { FirstFormant = int.Parse(range[0]), LastFormant = int.Parse(range[1]) };
            break;
        case "-median": CeilingScoring.Default = CeilingScoring.Default with { MedianStabilityWeight = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-relative": CeilingScoring.Default = CeilingScoring.Default with { Relative = true }; break;
        case "-floor": CeilingScoring.Default = CeilingScoring.Default with { StabilityFloor = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-continuity": CeilingScoring.Default = CeilingScoring.Default with { ContinuityWeight = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-continuity-to": CeilingScoring.Default = CeilingScoring.Default with { ContinuityLastFormant = int.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-continuity-deadzone": CeilingScoring.Default = CeilingScoring.Default with { ContinuityDeadZone = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-continuity-pause-to": CeilingScoring.Default = CeilingScoring.Default with { ContinuityPauseLastFormant = int.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-continuity-within": CeilingScoring.Default = CeilingScoring.Default with { ContinuityWithinStretches = true }; break;
        case "-bandwidth-floor": CeilingScoring.Default = CeilingScoring.Default with { BandwidthFloorHz = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
        case "-list": files.AddRange(File.ReadAllLines(args[++i]).Select(l => l.Trim()).Where(l => l.Length > 0)); break;
        default: files.Add(args[i]); break;
    }
}
if (files.Count == 0 || (dumpDir is null && !live))
{
    Console.Error.WriteLine("usage: formant-check (-dump <dir> | -live) [-pieces] [-method Path|Legacy] [scoring options] [-jobs N] (-list files.txt | file.wav …)");
    return 1;
}

// Each voiced piece the path scored, as CeilingPathFinder does: per candidate log W, the medians over the piece of
// F1–F4 and B1–B4, and per pair of candidates the median |ΔF1| … |ΔF4|; then each vowel-core row on every candidate.
// Enough to emulate the path's scoring and decoding offline (HIGH-PITCH-PLAN §6 A).
static void AppendPieces(StringBuilder sb, TrackView tracks, CeilingPath path, IReadOnlyList<double> rowTimes)
{
    var analyses = tracks.FormantCandidates.Cast<IFormantFrames>().ToList();
    var k = analyses.Count;
    var reference = analyses[0];
    var voiced = new bool[reference.FrameCount];
    for (var i = 0; i < voiced.Length; i++)
        voiced[i] = tracks.Pitch.FrameCount > 0 && tracks.Pitch.IsVoiced(tracks.Pitch.Grid.NearestIndex(reference.FrameTime(i)));
    var list = new List<FramePiece>();
    FormantSmoothness.Pieces(voiced, list);
    var values = new List<double>();
    static double Median(List<double> v)
    {
        if (v.Count == 0) return double.NaN;
        v.Sort();
        return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
    }
    foreach (var piece in list)
    {
        double t0 = reference.FrameTime(piece.From), t1 = reference.FrameTime(piece.To - 1), mid = 0.5 * (t0 + t1);
        var segment = 0;
        for (var s = 0; s < path.SegmentCount; s++)
            if (!double.IsFinite(path.SegmentStart(s)) || path.SegmentStart(s) <= mid) segment = s;
        sb.Append($"q {t0:0.#####} {t1:0.#####} {tracks.FormantCeilings[path.SegmentCeiling(segment)]:0}");
        for (var c = 0; c < k; c++)
        {
            var to = Math.Max(piece.From, Math.Min(piece.To, analyses[c].FrameCount));
            var w = to - piece.From < FormantSmoothness.Terms + 3 ? double.NaN : FormantSmoothness.Criterion(analyses[c], piece.From, to);
            sb.Append($" {(double.IsFinite(w) && w > 0 ? Math.Log(w) : double.NaN):0.###}");
        }
        for (var c = 0; c < k; c++)
            for (var f = 0; f < 8; f++)
            {
                values.Clear();
                for (var i = piece.From; i < Math.Min(piece.To, analyses[c].FrameCount); i++)
                {
                    var frame = analyses[c].FrameAt(i);
                    if (f % 4 < frame.Length) values.Add(f < 4 ? frame[f].Frequency : frame[f - 4].Bandwidth);
                }
                sb.Append($" {Median(values):0.#}");
            }
        for (var c = 0; c < k; c++)
            for (var n = c + 1; n < k; n++)
                for (var f = 0; f < 4; f++)
                {
                    values.Clear();
                    for (var i = piece.From; i < Math.Min(piece.To, Math.Min(analyses[c].FrameCount, analyses[n].FrameCount)); i++)
                    {
                        var a = analyses[c].FrameAt(i);
                        var b = analyses[n].FrameAt(i);
                        if (f < a.Length && f < b.Length) values.Add(Math.Abs(a[f].Frequency - b[f].Frequency));
                    }
                    sb.Append($" {Median(values):0.#}");
                }
        sb.Append('\n');
    }
    foreach (var t in rowTimes)
    {
        sb.Append($"R {t:0.#####}");
        foreach (var candidate in tracks.FormantCandidates)
        {
            for (var f = 1; f <= 4; f++) sb.Append($" {candidate.ValueAtTime(f, t):0.#}");
            for (var f = 1; f <= 4; f++) sb.Append($" {candidate.BandwidthAtTime(f, t):0.#}");
        }
        sb.Append('\n');
    }
}

// Each vowel-core row (r) and each graph point of the F3 line (g) on every candidate: F1–F5 and B1–B5 (size research,
// HIGH-PITCH-PLAN §6 D′).
static void AppendCandidates(StringBuilder sb, TrackView tracks, IReadOnlyList<double> rowTimes)
{
    void Row(char kind, double t)
    {
        sb.Append($"K {kind} {t:0.#####}");
        foreach (var candidate in tracks.FormantCandidates)
        {
            for (var f = 1; f <= 5; f++) sb.Append($" {candidate.ValueAtTime(f, t):0.#}");
            for (var f = 1; f <= 5; f++) sb.Append($" {candidate.BandwidthAtTime(f, t):0.#}");
        }
        sb.Append('\n');
    }
    foreach (var t in rowTimes) Row('r', t);
    var series = FrameSeriesBuilder.ForTake(tracks);
    if (series.Series.TryGetValue(Reyfen.Timbratune.Core.Models.TakeSeries.F3, out var f3))
        for (var i = 0; i < f3.T.Count; i++)
            if (f3.Values[i] is not null) Row('g', f3.T[i]);
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
        if (pieces && tracks.CeilingPath is { } piecePath) AppendPieces(sb, tracks, piecePath, raw.Formants.Select(r => r.T).ToList());
        if (cands) AppendCandidates(sb, tracks, raw.Formants.Select(r => r.T).ToList());
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
    var liveView = provider.Update(signal.View(length), TrackSet.All, final: true)!.Value;
    var livePath = liveView.CeilingPath;

    var trimmed = new Sound(samples[..length], fsRate);
    var savedSw = Stopwatch.StartNew();
    var savedTracks = AcousticsAnalysisEngine.MeasureTracks(trimmed, RawAnalysisAssembler.LoudOutlierFraction, null, method);
    var saved = AnalysisPostProcessor.Process(RawAnalysisAssembler.Assemble(savedTracks)).Metrics;
    savedSw.Stop();
    if (Environment.GetEnvironmentVariable("FC_PATHS") is not null && livePath is not null && savedTracks.CeilingPath is { } sp)
    {
        string Describe(CeilingPath p) => string.Join(", ", Enumerable.Range(0, p.SegmentCount)
            .Select(k => $"{savedTracks.FormantCeilings[p.SegmentCeiling(k)]:0} from {p.SegmentStart(k):0.00}"));
        Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)}\n  saved: {Describe(sp)}\n  live:  {Describe(livePath)}");
        var fresh = new CeilingPath();
        new CeilingPathFinder().Find(liveView.FormantCandidates.Cast<IFormantFrames>().ToList(), liveView.Pitch,
            FormantCeilings.FallbackIndex(liveView.FormantCeilings), fresh);
        Console.WriteLine($"  live frames, fresh finder: {Describe(fresh)}");
    }
    if (Environment.GetEnvironmentVariable("FC_FRAMES") is not null)
    {
        // How far the live candidates' frames are from the saved ones (same frame times).
        var diffs = new List<double>();
        var sb = new StringBuilder();
        for (var c = 0; c < savedTracks.FormantCandidates.Count; c++)
        {
            var sf = (IFormantFrames)savedTracks.FormantCandidates[c];
            var lf = (IFormantFrames)liveView.FormantCandidates[c];
            int counted = 0, differ = 0, lengths = 0;
            for (var i = 0; i < Math.Min(sf.FrameCount, lf.FrameCount); i++)
            {
                var fa = sf.FrameAt(i);
                var fb = lf.FrameAt(i);
                if (Math.Abs(sf.FrameTime(i) - lf.FrameTime(i)) > 1e-6) { sb.Append($" t{i}"); break; }
                if (Environment.GetEnvironmentVariable("FC_VOICED") is not null && !savedTracks.Pitch.IsVoiced(savedTracks.Pitch.Grid.NearestIndex(sf.FrameTime(i)))) continue;
                if (fa.Length != fb.Length)
                {
                    lengths++;
                    if (c == 2 && lengths <= 12)
                        sb.Append($"\n        t {sf.FrameTime(i):0.00000}: saved {string.Join(" ", fa.ToArray().Select(v => $"{v.Frequency:0.0}/{v.Bandwidth:0}"))} | live {string.Join(" ", fb.ToArray().Select(v => $"{v.Frequency:0.0}/{v.Bandwidth:0}"))}");
                    continue;
                }
                var m = 0.0;
                for (var f = 0; f < Math.Min(4, fa.Length); f++) m = Math.Max(m, Math.Abs(fa[f].Frequency - fb[f].Frequency));
                counted++;
                if (m > 1) differ++;
            }
            sb.Append($" c{c}: frames {sf.FrameCount}/{lf.FrameCount}, differ >1 Hz {differ}/{counted}, other formant count {lengths}");
            if (c == 2)
            {
                // Per second: the median |ΔF2| and |ΔF3| and the share of frames with another formant count.
                for (var sec = 0; sec < (int)sf.FrameTime(sf.FrameCount - 1) + 1; sec++)
                {
                    var d = new List<double>();
                    var other = 0;
                    var all = 0;
                    for (var i = 0; i < Math.Min(sf.FrameCount, lf.FrameCount); i++)
                    {
                        if ((int)sf.FrameTime(i) != sec) continue;
                        all++;
                        var x = sf.FrameAt(i);
                        var y = lf.FrameAt(i);
                        if (x.Length != y.Length) { other++; continue; }
                        if (x.Length >= 3) d.Add(Math.Abs(x[1].Frequency - y[1].Frequency) + Math.Abs(x[2].Frequency - y[2].Frequency));
                    }
                    d.Sort();
                    sb.Append($"\n      {sec} s: median |dF2|+|dF3| {(d.Count > 0 ? d[d.Count / 2] : double.NaN):0.0} Hz, other count {other}/{all}");
                }
            }
        }
        var voicedDiffer = 0;
        for (var i = 0; i < Math.Min(savedTracks.Pitch.FrameCount, liveView.Pitch.FrameCount); i++)
            if (savedTracks.Pitch.IsVoiced(i) != liveView.Pitch.IsVoiced(i)) voicedDiffer++;
        Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)}: voicing differs {voicedDiffer};{sb}");
    }
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
