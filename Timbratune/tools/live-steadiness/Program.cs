// Streams a WAV through LiveAnalyzer in 100 ms ticks (as the app does) and reports, per
// live graph, how steady and how accurate its current dot is:
//   dot step    — mean |change of the dot| between ticks
//   tail change — mean |revision| of the last 1 s of the line between ticks
//   dot error   — mean |dot − final line at the same time| (final = after the last tick;
//                 for pitch, the exact final contour)
// all as % of the graph's range. "baseline" = the smoothing without prediction (newest
// value held); "app" = the settings the app uses (LiveTimelinesViewModel).
using Reyfen.Timbratune.Analysis;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.ViewModels;
using System.Globalization;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run -c Release -- take.wav [formantBlockSeconds]");
    return 2;
}
Reyfen.Timbratune.Acoustics.Sound sound;
using (var f = File.OpenRead(args[0])) sound = Reyfen.Timbratune.Acoustics.WavDecoder.Decode(f);
var fs = sound.SamplingFrequency;
var mono = sound.ToMono().ToArray();
var block = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : LiveAnalyzer.DefaultFormantBlockSeconds;
var live = new LiveAnalyzer(fs, formantBlockSeconds: block);
var sw = System.Diagnostics.Stopwatch.StartNew();
var ticks = new List<LiveSnapshot>();
for (var i = 0; i < mono.Length; i += (int)(fs / 10))
{
    live.Append(mono.AsSpan(i, Math.Min((int)(fs / 10), mono.Length - i)));
    ticks.Add(live.Update());
}
var final = ticks[^1];
Console.WriteLine($"formant block {block} s: {sw.Elapsed.TotalSeconds:0.00} s of updates for {mono.Length / fs:0.0} s of audio");

static IReadOnlyList<TimedValue> Pitch(LiveSnapshot s)
{
    var r = new List<TimedValue>();
    if (s.Result?.Detail is not { } d) return r;
    for (var i = 0; i < d.Frames.T.Count; i++) if (d.Frames.Hz[i] is { } hz) r.Add(new TimedValue(d.Frames.T[i], hz));
    return r;
}

var metrics = new (string Name, Func<LiveSnapshot, IReadOnlyList<TimedValue>> Get, SmoothingSpec Spec, double Range)[]
{
    ("pitch", Pitch, LiveTimelinesViewModel.PitchSmoothing, 260),
    ("loudness", s => s.Series.Loudness, LiveTimelinesViewModel.LoudnessSmoothing, 33),
    ("hnr", s => s.Series.Hnr, LiveTimelinesViewModel.FrameSmoothing, 30),
    ("f2", s => s.Series.F2, LiveTimelinesViewModel.VowelSmoothing, 900),
    ("f3", s => s.Series.F3, LiveTimelinesViewModel.VowelSmoothing, 1300),
    ("weight", s => s.Series.Weight, LiveTimelinesViewModel.VowelSmoothing, 20),
};
var variants = new (string Name, Func<SmoothingSpec, SmoothingSpec> Make)[]
{
    ("baseline", s => s with { TrendDamping = 0, MinimumTrust = 1 }),
    ("app", s => s),
};

Console.WriteLine("metric    variant     dot step  tail change  dot error   (% of range)");
foreach (var m in metrics)
{
    var finalLine = (m.Name == "pitch" ? m.Get(final) : RecentWindow.Smooth(m.Get(final), m.Spec))
        .Where(x => !double.IsNaN(x.Value)).ToList();
    foreach (var (name, make) in variants)
    {
        var spec = make(m.Spec);
        double steps = 0, err = 0, tail = 0;
        int nSteps = 0, nErr = 0, nTail = 0;
        TimedValue? prev = null;
        Dictionary<long, double>? prevLine = null;
        foreach (var tick in ticks)
        {
            var line = RecentWindow.Smooth(m.Get(tick), spec, 0, tick.Elapsed);
            var cur = new Dictionary<long, double>();
            foreach (var x in line)
                if (!double.IsNaN(x.Value) && x.T > tick.Elapsed - 1.0) cur[(long)Math.Round(x.T * 1e4)] = x.Value;
            if (prevLine is not null)
                foreach (var (k, v) in cur)
                    if (prevLine.TryGetValue(k, out var pv)) { tail += Math.Abs(v - pv); nTail++; }
            prevLine = cur;

            TimedValue? dot = null;
            for (var i = line.Count - 1; i >= 0; i--) if (!double.IsNaN(line[i].Value)) { dot = line[i]; break; }
            if (dot is not { } d || tick.Elapsed - d.T > 0.3) { prev = null; continue; } // paused: no current point
            if (prev is { } p) { steps += Math.Abs(d.Value - p.Value); nSteps++; }
            prev = d;
            if (finalLine.Count == 0) continue;
            var near = finalLine.MinBy(x => Math.Abs(x.T - d.T));
            if (Math.Abs(near.T - d.T) < 0.06) { err += Math.Abs(near.Value - d.Value); nErr++; }
        }
        Console.WriteLine($"{m.Name,-9} {name,-10} {100 * steps / Math.Max(1, nSteps) / m.Range,8:0.00}  {100 * tail / Math.Max(1, nTail) / m.Range,10:0.00}  {100 * err / Math.Max(1, nErr) / m.Range,9:0.00}");
    }
}
return 0;
