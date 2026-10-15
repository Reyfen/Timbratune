using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>
/// Golden snapshots for the refactoring (REFACTOR-PLAN.md, P0): for each fixture, the saved
/// analysis as the store writes it (take metrics, detail.json, series.json) and the final
/// live snapshot (metrics, detail and the live series), compared with files produced by the
/// code before the refactoring. Saved values must stay identical; live values may move by
/// at most 1e-7 (last-digit rounding, as in the b5d73f4 comparison, HANDOFF §8d).
/// Set TIMBRATUNE_UPDATE_GOLDEN=1 to (re)write the files in Golden/ — only on purpose.
/// </summary>
public sealed class GoldenTests
{
    private const double LiveTolerance = 1e-7;

    public static TheoryData<string> Clips => new() { "vctk_f294", "vctk_f339", "vctk_m311", "vctk_m345" };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string FixturePath(string clip) => Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav");

    [Theory, MemberData(nameof(Clips))]
    public async Task SavedAnalysisMatchesTheGoldenFiles(string clip)
    {
        var result = await new AcousticsAnalysisEngine().AnalyzeAsync(FixturePath(clip));
        Check(clip + ".take.json", TimbratuneJson.WriteRecordings([result.Metrics]), 0);
        Check(clip + ".detail.json", TimbratuneJson.WriteDetail(result.Detail), 0);
        using var series = new MemoryStream();
        TimbratuneJson.WriteSeries(series, result.Series!);
        Check(clip + ".series.json", Encoding.UTF8.GetString(series.ToArray()), 0);
    }

    [Theory, MemberData(nameof(Clips))]
    public void FinalLiveSnapshotMatchesTheGoldenFile(string clip)
    {
        double[] samples;
        using (var s = File.OpenRead(FixturePath(clip))) samples = WavDecoder.Decode(s).Channel(0).ToArray();
        var final = RecordLikeTheApp(samples);
        Assert.NotNull(final.Result);
        Check(clip + ".live.json", LiveJson(final), LiveTolerance);
    }

    /// <summary>
    /// Streams the samples as the app records: irregular chunks, an update every 100 ms of
    /// audio, a full update about once a second and quick ones in between, then the final
    /// update on the aligned length.
    /// </summary>
    internal static LiveSnapshot RecordLikeTheApp(double[] samples)
    {
        var live = new LiveAnalyzer(44100);
        var rng = new Random(11);
        var next = 4410;
        var tick = 0;
        for (var i = 0; i < samples.Length;)
        {
            var n = Math.Min(rng.Next(200, 1500), samples.Length - i);
            live.Append(samples.AsSpan(i, n));
            i += n;
            if (i < next) continue;
            live.Update(full: tick++ % 10 == 0);
            next += 4410;
        }
        return live.Update(final: true, length: live.AlignedLength(live.SampleCount));
    }

    private static string LiveJson(LiveSnapshot s)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("elapsed", s.Elapsed);
            w.WritePropertyName("metrics");
            using (var m = JsonDocument.Parse(TimbratuneJson.WriteRecordings([s.Result!.Metrics]))) m.RootElement[0].WriteTo(w);
            w.WritePropertyName("detail");
            using (var d = JsonDocument.Parse(TimbratuneJson.WriteDetail(s.Result.Detail))) d.RootElement.WriteTo(w);
            w.WriteStartObject("series");
            foreach (var (name, points) in new[]
                     {
                         ("loudness", s.Series.Loudness), ("hnr", s.Series.Hnr), ("f2", s.Series.F2), ("f3", s.Series.F3),
                         ("weight", s.Series.Weight), ("jitter", s.Series.Jitter),
                     })
            {
                w.WriteStartObject(name);
                w.WriteStartArray("t");
                foreach (var p in points) w.WriteNumberValue(p.T);
                w.WriteEndArray();
                w.WriteStartArray("v");
                foreach (var p in points) w.WriteNumberValue(p.Value);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Check(string name, string actualJson, double tolerance)
    {
        // Stored pretty-printed so a change shows up as a readable diff.
        string actual;
        using (var doc = JsonDocument.Parse(actualJson)) actual = JsonSerializer.Serialize(doc.RootElement, Pretty);
        var path = Path.Combine(GoldenDir(), name);
        if (Environment.GetEnvironmentVariable("TIMBRATUNE_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual.ReplaceLineEndings("\n") + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing golden file {name}; run once with TIMBRATUNE_UPDATE_GOLDEN=1 on the base commit");
        using var expected = JsonDocument.Parse(File.ReadAllText(path));
        using var got = JsonDocument.Parse(actual);
        var differences = new List<string>();
        Compare(expected.RootElement, got.RootElement, "$", tolerance, differences);
        Assert.True(differences.Count == 0,
            $"{name}: {differences.Count} difference(s) from the golden file (tolerance {tolerance}):\n" + string.Join("\n", differences.Take(20)));
    }

    private static void Compare(JsonElement e, JsonElement a, string at, double tolerance, List<string> differences)
    {
        if (e.ValueKind != a.ValueKind)
        {
            differences.Add($"{at}: {e.ValueKind} expected, got {a.ValueKind}");
            return;
        }
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedNames = e.EnumerateObject().Select(p => p.Name).ToList();
                var actualNames = a.EnumerateObject().Select(p => p.Name).ToList();
                if (!expectedNames.SequenceEqual(actualNames))
                {
                    differences.Add($"{at}: keys [{string.Join(", ", expectedNames)}] expected, got [{string.Join(", ", actualNames)}]");
                    return;
                }
                foreach (var p in e.EnumerateObject()) Compare(p.Value, a.GetProperty(p.Name), at + "." + p.Name, tolerance, differences);
                break;
            case JsonValueKind.Array:
                if (e.GetArrayLength() != a.GetArrayLength())
                {
                    differences.Add($"{at}: {e.GetArrayLength()} items expected, got {a.GetArrayLength()}");
                    return;
                }
                for (var i = 0; i < e.GetArrayLength(); i++) Compare(e[i], a[i], $"{at}[{i}]", tolerance, differences);
                break;
            case JsonValueKind.Number:
                var x = e.GetDouble();
                var y = a.GetDouble();
                if (tolerance == 0 ? x != y : !(Math.Abs(x - y) <= tolerance)) differences.Add($"{at}: {x:R} expected, got {y:R}");
                break;
            default:
                if (e.GetRawText() != a.GetRawText()) differences.Add($"{at}: {e.GetRawText()} expected, got {a.GetRawText()}");
                break;
        }
    }

    private static string GoldenDir([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "Golden");
}
