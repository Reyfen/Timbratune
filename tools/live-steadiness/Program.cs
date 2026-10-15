// Streams a WAV through LiveAnalyzer in 100 ms ticks (as the app does) and reports, per
// live graph, how steady and how accurate its current dot is:
//   dot step    — mean |change of the dot| between ticks
//   tail change — mean |revision| of the last 1 s of the line between ticks
//   dot error   — mean |dot − final line at the same time| (final = after the last tick;
//                 for pitch, the exact final contour)
// all as % of the graph's range. "baseline" = the smoothing without prediction (newest
// value held); "app" = the settings the app uses (LiveTimelinesViewModel).
//
// Each tick's lines are computed right after its update, as the app does: LiveAnalyzer
// refills its series lists in turn (two sets), so a snapshot's series are only valid until
// the update after next. Keeping all snapshots and reading them at the end measured the
// last update's data over and over (dots ≈ 0, loudness 24 %). The "app" lines are checked
// against LiveTimelinesViewModel.Compute on every tick, so the tool can't drift from it.
using Reyfen.Timbratune.Features.LiveMonitor;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Models;
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

// As LiveTimelinesViewModel.Compute: the contour of a quick update, else the result's detail.
static RecordingDetail? Detail(LiveSnapshot s) => s.Contour ?? s.Result?.Detail;

static IReadOnlyList<TimedValue> Pitch(LiveSnapshot s)
{
    var r = new List<TimedValue>();
    if (Detail(s) is not { } d) return r;
    for (var i = 0; i < d.Frames.T.Count && i < d.Frames.Hz.Count; i++) if (d.Frames.Hz[i] is { } hz) r.Add(new TimedValue(d.Frames.T[i], hz));
    return r;
}

// Line = index into Frame.Lines (−1 = the pitch line), to check the "app" lines against Compute.
var metrics = new (string Name, Func<LiveSnapshot, IReadOnlyList<TimedValue>> Get, SmoothingSpec Spec, double Range, int Line)[]
{
    ("pitch", Pitch, LiveTimelinesViewModel.PitchSmoothing, 260, -1),
    ("loudness", s => s.Series.Loudness, LiveTimelinesViewModel.LoudnessSmoothing, 33, 0),
    ("hnr", s => s.Series.Hnr, LiveTimelinesViewModel.FrameSmoothing, 30, 2),
    ("f2", s => s.Series.F2, LiveTimelinesViewModel.VowelSmoothing, 900, 3),
    ("f3", s => s.Series.F3, LiveTimelinesViewModel.VowelSmoothing, 1300, 4),
    ("weight", s => s.Series.Weight, LiveTimelinesViewModel.VowelSmoothing, 20, 5),
};
var variants = new (string Name, Func<SmoothingSpec, SmoothingSpec> Make)[]
{
    ("baseline", s => s with { TrendDamping = 0, MinimumTrust = 1 }),
    ("app", s => s),
};

// The whole take shown (axis from 0), as the app's default view.
static List<TimedValue> Line(IReadOnlyList<TimedValue> points, SmoothingSpec spec, LiveSnapshot s, bool pitch) => pitch
    ? RecentWindow.Smooth(points, spec, 0, s.Elapsed, Detail(s)?.Phrases.Select(p => p.End).ToList())
    : RecentWindow.Smooth(points, spec, 0, s.Elapsed);

var stats = new Stat[metrics.Length, variants.Length];
for (var m = 0; m < metrics.Length; m++)
    for (var v = 0; v < variants.Length; v++) stats[m, v] = new Stat();
var mismatches = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
LiveSnapshot final = LiveSnapshot.Empty;
var step = (int)(fs / 10);
for (var i = 0; i < mono.Length; i += step)
{
    live.Append(mono.AsSpan(i, Math.Min(step, mono.Length - i)));
    var tick = live.Update();
    final = tick;
    var frame = LiveTimelinesViewModel.Compute(tick);
    for (var m = 0; m < metrics.Length; m++)
    {
        var points = metrics[m].Get(tick);
        for (var v = 0; v < variants.Length; v++)
        {
            var spec = variants[v].Make(metrics[m].Spec);
            var line = Line(points, spec, tick, metrics[m].Line < 0);
            if (spec == metrics[m].Spec && !Same(line, metrics[m].Line < 0 ? frame.PitchLine : frame.Lines[metrics[m].Line])) mismatches++;
            stats[m, v].Add(line, tick.Elapsed);
        }
    }
}
Console.WriteLine($"formant block {block} s: {sw.Elapsed.TotalSeconds:0.00} s of updates for {mono.Length / fs:0.0} s of audio");
if (mismatches > 0) Console.WriteLine($"WARNING: {mismatches} 'app' lines differ from LiveTimelinesViewModel.Compute; the tool no longer measures what the app draws");

Console.WriteLine("metric    variant     dot step  tail change  dot error   (% of range)");
for (var m = 0; m < metrics.Length; m++)
{
    var finalLine = (metrics[m].Line < 0 ? metrics[m].Get(final) : RecentWindow.Smooth(metrics[m].Get(final), metrics[m].Spec))
        .Where(x => !double.IsNaN(x.Value)).ToList();
    for (var v = 0; v < variants.Length; v++)
    {
        var (steps, tail, err) = stats[m, v].Result(finalLine);
        var r = metrics[m].Range;
        Console.WriteLine($"{metrics[m].Name,-9} {variants[v].Name,-10} {100 * steps / r,8:0.00}  {100 * tail / r,10:0.00}  {100 * err / r,9:0.00}");
    }
}
return 0;

static bool Same(IReadOnlyList<TimedValue> a, IReadOnlyList<TimedValue> b)
{
    if (a.Count != b.Count) return false;
    for (var i = 0; i < a.Count; i++)
        if (a[i].T != b[i].T || !(a[i].Value == b[i].Value || double.IsNaN(a[i].Value) && double.IsNaN(b[i].Value))) return false;
    return true;
}

/// <summary>One metric's running numbers under one smoothing, fed tick by tick.</summary>
sealed class Stat
{
    private double _steps, _tail;
    private int _nSteps, _nTail;
    private TimedValue? _prev;
    private Dictionary<long, double>? _prevLine;
    private readonly List<TimedValue> _dots = [];

    public void Add(List<TimedValue> line, double elapsed)
    {
        var cur = new Dictionary<long, double>();
        foreach (var x in line)
            if (!double.IsNaN(x.Value) && x.T > elapsed - 1.0) cur[(long)Math.Round(x.T * 1e4)] = x.Value;
        if (_prevLine is not null)
            foreach (var (k, v) in cur)
                if (_prevLine.TryGetValue(k, out var pv)) { _tail += Math.Abs(v - pv); _nTail++; }
        _prevLine = cur;

        TimedValue? dot = null;
        for (var i = line.Count - 1; i >= 0; i--) if (!double.IsNaN(line[i].Value)) { dot = line[i]; break; }
        if (dot is not { } d || elapsed - d.T > 0.3) { _prev = null; return; } // paused: no current point
        if (_prev is { } p) { _steps += Math.Abs(d.Value - p.Value); _nSteps++; }
        _prev = d;
        _dots.Add(d);
    }

    /// <summary>Mean dot step, mean tail change and mean dot error against <paramref name="finalLine"/>.</summary>
    public (double Steps, double Tail, double Error) Result(List<TimedValue> finalLine)
    {
        double err = 0;
        var nErr = 0;
        if (finalLine.Count > 0)
            foreach (var d in _dots)
            {
                var near = finalLine.MinBy(x => Math.Abs(x.T - d.T));
                if (Math.Abs(near.T - d.T) < 0.06) { err += Math.Abs(near.Value - d.Value); nErr++; }
            }
        return (_steps / Math.Max(1, _nSteps), _tail / Math.Max(1, _nTail), err / Math.Max(1, nErr));
    }
}
