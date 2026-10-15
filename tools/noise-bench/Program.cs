// Analyses every *.wav under a folder (recursively) with the full analysis, in parallel, and
// writes one CSV row of take metrics per file. score.py compares the rows of noisy / cleaned
// files with the row of their clean source.
using Reyfen.Timbratune.Core.Analysis;
using System.Collections.Concurrent;
using System.Globalization;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run -c Release -- folder out.csv");
    return 2;
}
var root = Path.GetFullPath(args[0]);
var files = Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
var engine = new AcousticsAnalysisEngine();
var rows = new ConcurrentDictionary<string, string>();
var done = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (file, ct) =>
{
    var name = Path.GetRelativePath(root, file).Replace('\\', '/');
    try
    {
        var m = (await engine.AnalyzeAsync(file, cancellationToken: ct)).Metrics;
        double?[] values =
        [
            m.Pitch.MeanHz, m.Pitch.MedianHz, m.Pitch.SdHz, m.Pitch.MaxHz, m.Register?.InRegisterPct,
            m.Formants.F1Hz, m.Formants.F2Hz, m.Formants.F3Hz, m.Formants.UsablePct,
            m.VoiceQuality.HnrDb, m.VoiceQuality.JitterPct, m.VoiceQuality.ShimmerPct,
            m.Weight?.H1a3cDb, m.Weight?.H1a3Db, m.Weight?.TiltDbKhz, m.Intensity.MeanDb,
        ];
        rows[name] = name + "," + string.Join(",", values.Select(v => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? ""));
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"{name}: {e.Message}");
    }
    var n = Interlocked.Increment(ref done);
    if (n % 50 == 0) Console.Error.WriteLine($"{n}/{files.Count}  {sw.Elapsed.TotalSeconds:0}s");
});
using var w = new StreamWriter(args[1]);
w.WriteLine("file,pitch_mean,pitch_median,pitch_sd,pitch_max,in_register,f1,f2,f3,formants_usable,hnr,jitter,shimmer,weight,h1a3,tilt,loudness");
foreach (var key in rows.Keys.Order(StringComparer.Ordinal)) w.WriteLine(rows[key]);
Console.Error.WriteLine($"{rows.Count} files in {sw.Elapsed.TotalSeconds:0.0}s -> {args[1]}");
return 0;
