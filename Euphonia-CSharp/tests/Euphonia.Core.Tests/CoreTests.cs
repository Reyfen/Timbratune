using Euphonia.Core.Analysis;
using Euphonia.Core.Domain;
using Euphonia.Core.Json;
using Euphonia.Core.Models;
using Euphonia.Core.Storage;

namespace Euphonia.Core.Tests;

public sealed class ZonesTests
{
    [Theory]
    [InlineData(120, "masc")]
    [InlineData(145, "neutral")]  // from is inclusive
    [InlineData(164.99, "neutral")]
    [InlineData(165, "fem")]
    [InlineData(300, "fem")]      // above every range → last zone
    [InlineData(95, "fem")]       // below every range → ALSO last zone (zones.ts quirk, kept on purpose)
    public void PitchZoneOf(double hz, string expected) =>
        Assert.Equal(expected, Zones.ZoneOf(Zones.Pitch, hz)!.Name);

    [Fact]
    public void ZoneOfNullIsNull() => Assert.Null(Zones.ZoneOf(Zones.Weight, null));

    [Theory]
    [InlineData(null, "", "—")]
    [InlineData(165.23, " Hz", "165.23 Hz")]
    [InlineData(130.0, "", "130")]
    public void FmtMatchesJavaScript(double? v, string unit, string expected) =>
        Assert.Equal(expected, Zones.Fmt(v, unit));

    [Fact]
    public void RegistryCoversEveryMetricKey() =>
        Assert.Equal(Enum.GetValues<MetricKey>().Length, Metrics.All.Count);
}

public sealed class JsonTests
{
    [Fact]
    public void ReadsAnalyzePyOutputWithSnakeCaseNames()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f294.expected.json"));
        var r = EuphoniaJson.ReadRecordings(json).Single();
        Assert.Equal(1, r.Id);
        Assert.Equal("audio/001.wav", r.Audio);
        Assert.Equal(162.82, r.Pitch.MeanHz);
        Assert.Equal(1391.1, r.Formants.F2Hz);
        Assert.Equal(5.5, r.Weight!.H1a3cDb);
        Assert.Equal(-7.35, r.Weight.TiltDbKhz);
        Assert.Equal(40, r.Register!.PhrasesLandedPct);
        Assert.Equal(5, r.Register.NPhrases);
    }

    [Fact]
    public void WritesTheSameKeysAnalyzePyWrites()
    {
        var json = EuphoniaJson.WriteRecordings([new Recording { Weight = new Weight(), Register = new Register() }]);
        foreach (var key in new[] { "\"source_file\"", "\"duration_s\"", "\"f2_hz\"", "\"voice_quality\"", "\"jitter_pct\"",
                     "\"h1a3c_db\"", "\"tilt_db_khz\"", "\"in_register_semitones_sd\"", "\"n_phrases\"", "\"phrases_landed_pct\"" })
            Assert.Contains(key, json);
    }

    [Fact]
    public void DetailRoundTripsNullFrames()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f294.expected-detail.json"));
        var d = EuphoniaJson.ReadDetail(json)!;
        Assert.Equal(d.Frames.T.Count, d.Frames.Hz.Count);
        Assert.Contains(d.Frames.Hz, h => h is null);
        var again = EuphoniaJson.ReadDetail(EuphoniaJson.WriteDetail(d))!;
        Assert.Equal(d.Frames.Hz, again.Frames.Hz);
        Assert.Equal(d.Phrases.Count, again.Phrases.Count);
    }
}

public sealed class RecordingStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "euphonia-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AddAssignsIdsCopiesAudioAndDeleteRemovesEverything()
    {
        var store = new RecordingStore(new DataPaths(_root));
        Assert.Empty(store.Load());

        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        var first = store.Add(new Recording { Label = "a" }, new RecordingDetail(), wav);
        var second = store.Add(new Recording { Label = "b" }, new RecordingDetail(), wav);

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal("audio/002.wav", second.Audio);
        Assert.True(File.Exists(Path.Combine(_root, "audio", "002.wav")));
        Assert.True(File.Exists(Path.Combine(_root, "analysis", "2.json")));
        Assert.NotNull(store.LoadDetail(second));

        store.Delete(1);
        var left = store.Load();
        Assert.Equal([2], left.Select(r => r.Id));
        Assert.False(File.Exists(Path.Combine(_root, "audio", "001.wav")));

        // ids keep counting from the max, like analyze.py
        Assert.Equal(3, store.Add(new Recording(), new RecordingDetail(), wav).Id);
    }

    [Fact]
    public void ResolveRefusesPathsOutsideTheRoot()
    {
        var paths = new DataPaths(_root);
        Assert.NotNull(paths.Resolve("audio/001.wav"));
        Assert.Null(paths.Resolve("../outside.wav"));
    }
}

public sealed class PostProcessorTests
{
    [Fact]
    public void MedianMatchesPythonStatistics()
    {
        Assert.Equal(2.5, AnalysisPostProcessor.Median([4, 1, 3, 2]));
        Assert.Equal(3, AnalysisPostProcessor.Median([5, 3, 1]));
        Assert.Null(AnalysisPostProcessor.Median([]));
    }

    [Fact]
    public void CleanRoundsToTwoPlacesAndDropsNonFinite()
    {
        Assert.Equal(1.23, AnalysisPostProcessor.Clean(1.2345));
        Assert.Null(AnalysisPostProcessor.Clean(double.NaN));
        Assert.Null(AnalysisPostProcessor.Clean(double.PositiveInfinity));
    }

    [Fact]
    public void StabilityGateAdvancesPreviousF2EvenWhenDropping()
    {
        // 1500 → 1800 (jump, dropped, prev = 1800) → 1850 (kept: |1850-1800| ≤ 150)
        var rows = new[] { 1500.0, 1800, 1850, 1860, 1870, 1880 }
            .Select((f2, i) => new PraatOutput.FormantRow(5500, i * 0.01, 500, f2, 2800)).ToList();
        var (_, f2Median, _, kept) = AnalysisPostProcessor.VowelFormants(rows);
        Assert.Equal(1860, f2Median); // median of 1500, 1850, 1860, 1870, 1880
        Assert.Equal(5, kept.Count);
    }

    [Fact]
    public void PhraseBreakdownSplitsFramesByPhrase()
    {
        var detail = new RecordingDetail
        {
            RegisterFloorHz = 130,
            Frames = new Frames
            {
                T = [0.0, 0.1, 0.2, 1.0, 1.1, 1.2],
                Hz = [200, 220, null, 120, 140, 160],
            },
            Phrases =
            [
                new Phrase { Start = 0, End = 0.25, OffsetHz = 220 },
                new Phrase { Start = 0.9, End = 1.25, OffsetHz = 150 },
            ],
        };
        var result = AnalysisPostProcessor.PhraseBreakdown(detail,
            f2Frames: [(0.05, 1500), (0.15, 1600), (1.05, 1300)],
            weightFrames: [(1.1, 12.0), (1.2, 14.0)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(210, result[0].MeanHz);
        Assert.Equal(1550, result[0].F2Hz);
        Assert.Null(result[0].WeightDb);
        Assert.Equal(140, result[1].MeanHz);
        Assert.Equal(13, result[1].WeightDb);
        Assert.Equal(150, result[1].OffsetHz);
        // melody uses in-register frames only: 140 and 160 Hz (120 is below the 130 Hz floor)
        Assert.NotNull(result[1].MelodySt);

        // without F2/weight frames (older takes) those stay empty
        Assert.All(AnalysisPostProcessor.PhraseBreakdown(detail), p => Assert.Null(p.F2Hz));
    }

    [Fact]
    public void PraatUndefinedParsesAsNaN() =>
        Assert.True(double.IsNaN(PraatOutput.ParseNumber("--undefined--")));

    [Fact]
    public void ParseRequiresEndMarker() =>
        Assert.Throws<PraatException>(() => PraatOutput.Parse("S\tduration\t1\n"));
}
