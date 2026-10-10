using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.Core.Tests;

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
        var r = TimbratuneJson.ReadRecordings(json).Single();
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
        var json = TimbratuneJson.WriteRecordings([new Recording { Weight = new Weight(), Register = new Register() }]);
        foreach (var key in new[] { "\"source_file\"", "\"duration_s\"", "\"f2_hz\"", "\"voice_quality\"", "\"jitter_pct\"",
                     "\"h1a3c_db\"", "\"tilt_db_khz\"", "\"in_register_semitones_sd\"", "\"n_phrases\"", "\"phrases_landed_pct\"" })
            Assert.Contains(key, json);
    }

    [Fact]
    public void DetailRoundTripsNullFrames()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f294.expected-detail.json"));
        var d = TimbratuneJson.ReadDetail(json)!;
        Assert.Equal(d.Frames.T.Count, d.Frames.Hz.Count);
        Assert.Contains(d.Frames.Hz, h => h is null);
        var again = TimbratuneJson.ReadDetail(TimbratuneJson.WriteDetail(d))!;
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
    public void AddMakesOneFolderPerTakeAndDeleteRemovesIt()
    {
        var store = new RecordingStore(new DataPaths(_root));
        Assert.Empty(store.Load());

        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        var first = store.Add(new Recording { Label = "a" }, new RecordingDetail(), wav);
        var second = store.Add(new Recording { Label = "b: c/d" }, new RecordingDetail(), wav);

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal("takes/002 b_ c_d/take.wav", second.Audio);
        var folder = Path.Combine(_root, "takes", "002 b_ c_d");
        foreach (var file in new[] { "take.wav", "take.json", "detail.json" }) Assert.True(File.Exists(Path.Combine(folder, file)), file);
        Assert.NotNull(store.LoadDetail(second));
        Assert.Equal(["a", "b: c/d"], store.Load().Select(r => r.Label));

        store.Delete(1);
        Assert.Equal([2], store.Load().Select(r => r.Id));
        Assert.False(Directory.Exists(Path.Combine(_root, "takes", "001 a")));

        // ids keep counting from the max
        Assert.Equal(3, store.Add(new Recording(), new RecordingDetail(), wav).Id);
    }

    [Fact]
    public void TakeFoldersCanBeRemovedAndCopiedInByHand()
    {
        var store = new RecordingStore(new DataPaths(_root));
        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        var a = store.Add(new Recording { Label = "a" }, new RecordingDetail(), wav);
        store.Add(new Recording { Label = "b" }, new RecordingDetail(), wav);

        // a copy of take 1's folder (e.g. from another device) gets the next free id
        CopyFolder(store.FolderOf(a), Path.Combine(_root, "takes", "001 a copy"));
        var takes = store.Load();
        Assert.Equal([1, 2, 3], takes.Select(r => r.Id));
        Assert.Equal(["a", "b", "a"], takes.Select(r => r.Label));
        Assert.Equal([1, 2, 3], store.Load().Select(r => r.Id)); // the new id is kept

        // removing a folder by hand removes the take; a folder without take.json isn't one
        Directory.Delete(Path.Combine(_root, "takes", "002 b"), true);
        Directory.CreateDirectory(Path.Combine(_root, "takes", "notes"));
        Assert.Equal([1, 3], store.Load().Select(r => r.Id));

        // an unreadable take.json is reported, not fatal
        Directory.CreateDirectory(Path.Combine(_root, "takes", "broken"));
        File.WriteAllText(Path.Combine(_root, "takes", "broken", "take.json"), "{ not json");
        Assert.Equal([1, 3], store.Load().Select(r => r.Id));
        Assert.Equal(["broken"], store.Unreadable);
    }

    [Fact]
    public void TakesKeepWhenTheyWereRecorded()
    {
        var store = new RecordingStore(new DataPaths(_root));
        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        var stamped = store.Add(new Recording { Label = "a", Date = "2026-10-07", RecordedAt = "2026-10-07T14:03:12+03:00" },
            new RecordingDetail(), wav);
        Assert.Equal("2026-10-07T14:03:12+03:00", store.Load().Single(r => r.Id == stamped.Id).RecordedAt);

        // An older take without a time: its audio file's time, when that's on the take's date.
        var older = store.Add(new Recording { Label = "b", Date = "2026-09-01" }, new RecordingDetail(), wav);
        var audio = store.Paths.Resolve(older.Audio)!;
        File.SetLastWriteTime(audio, new DateTime(2026, 9, 1, 9, 30, 0));
        Assert.Equal(Recording.Timestamp(new DateTime(2026, 9, 1, 9, 30, 0)), store.Load().Single(r => r.Id == older.Id).RecordedAt);
        File.SetLastWriteTime(audio, new DateTime(2026, 9, 5, 9, 30, 0));
        Assert.Null(store.Load().Single(r => r.Id == older.Id).RecordedAt); // another day: no time is better than a wrong one
    }

    [Fact]
    public void RenamingChangesTheLabelAndTheFolderName()
    {
        var store = new RecordingStore(new DataPaths(_root));
        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        var take = store.Add(new Recording { Label = "untitled take", Pitch = new Pitch { MeanHz = 200 } }, new RecordingDetail(), wav);

        var renamed = store.Rename(take, "  rainbow: passage  ");
        Assert.Equal("rainbow: passage", renamed.Label);
        Assert.Equal("takes/001 rainbow_ passage/take.wav", renamed.Audio);
        var loaded = Assert.Single(store.Load());
        Assert.Equal(("rainbow: passage", 1, 200.0), (loaded.Label, loaded.Id, loaded.Pitch.MeanHz));
        Assert.False(Directory.Exists(Path.Combine(_root, "takes", "001 untitled take")));

        // without renaming the folder (e.g. while its audio plays) only the label changes
        var kept = store.Rename(loaded, "second name", renameFolder: false);
        Assert.Equal("second name", Assert.Single(store.Load()).Label);
        Assert.Equal("takes/001 rainbow_ passage/take.wav", kept.Audio);
    }

    [Fact]
    public void TheOldLayoutIsConvertedToTakeFolders()
    {
        // recordings.json + audio/ + analysis/, as versions before 0.2.0 wrote them
        var paths = new DataPaths(_root);
        Directory.CreateDirectory(paths.AudioDir);
        Directory.CreateDirectory(paths.AnalysisDir);
        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        File.Copy(wav, Path.Combine(paths.AudioDir, "004.wav"));
        var detail = new RecordingDetail { DurationS = 1.5, RegisterFloorHz = 140 };
        File.WriteAllText(Path.Combine(paths.AnalysisDir, "4.json"), TimbratuneJson.WriteDetail(detail));
        File.WriteAllText(Path.Combine(paths.AnalysisDir, "4.series.json"), "{\"version\":1,\"series\":{}}");
        var old = new Recording
        {
            Id = 4, Label = "old take", Note = "n", Date = "2026-09-01", Audio = "audio/004.wav", Detail = "analysis/4.json",
            DurationS = 1.5, Pitch = new Pitch { MeanHz = 201.5 },
        };
        File.WriteAllText(paths.RecordingsJson, TimbratuneJson.WriteRecordings([old, new Recording { Id = 7, Label = "no audio" }]));

        var store = new RecordingStore(paths);
        var takes = store.Load();

        Assert.Equal([4, 7], takes.Select(r => r.Id));
        var take = takes[0];
        Assert.Equal(("old take", "n", "2026-09-01", 201.5), (take.Label, take.Note, take.Date, take.Pitch.MeanHz));
        Assert.Equal(File.ReadAllBytes(wav), File.ReadAllBytes(paths.Resolve(take.Audio)!));
        Assert.Equal(140, store.LoadDetail(take)!.RegisterFloorHz);
        Assert.True(store.HasSeries(take));
        Assert.Null(takes[1].Audio);

        Assert.False(File.Exists(paths.RecordingsJson));
        Assert.True(File.Exists(paths.RecordingsJson + ".migrated"));
        Assert.False(Directory.Exists(paths.AudioDir));
        Assert.False(Directory.Exists(paths.AnalysisDir));
        Assert.Equal([4, 7], store.Load().Select(r => r.Id)); // converting is done once
    }

    [Fact]
    public void MovingTheDataFolderNeverMergesTwoFoldersWithTakes()
    {
        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");
        var from = Path.Combine(_root, "private");
        var to = Path.Combine(_root, "shared");
        new RecordingStore(new DataPaths(from)).Add(new Recording { Label = "a" }, new RecordingDetail(), wav);

        DataPaths.MoveContents(from, to);
        Assert.Equal(["a"], new RecordingStore(new DataPaths(to)).Load().Select(r => r.Label));
        Assert.False(Directory.Exists(Path.Combine(from, "takes")));
        Assert.False(File.Exists(Path.Combine(to, ".moving")));

        // takes in both: left alone
        new RecordingStore(new DataPaths(from)).Add(new Recording { Label = "b" }, new RecordingDetail(), wav);
        DataPaths.MoveContents(from, to);
        Assert.Single(new RecordingStore(new DataPaths(from)).Load());
        Assert.Single(new RecordingStore(new DataPaths(to)).Load());
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
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
            .Select((f2, i) => new RawAnalysis.FormantRow(5500, i * 0.01, 500, f2, 2800)).ToList();
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
        Assert.True(double.IsNaN(PraatReference.PraatAnalysisEngine.ParseNumber("--undefined--")));

    [Fact]
    public void ParseRequiresEndMarker() =>
        Assert.Throws<PraatReference.PraatException>(() => PraatReference.PraatAnalysisEngine.Parse("S\tduration\t1\n"));
}
