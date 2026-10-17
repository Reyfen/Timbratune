using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>Old takes keep their numbers; their re-analysis is kept beside them (RESONANCE-PLAN F7).</summary>
public sealed class ReanalysisTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-reanalysis-" + Guid.NewGuid().ToString("N"));
    private static readonly string Wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f294.wav");

    public ReanalysisTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static readonly AcousticsAnalysisEngine Legacy = new() { FormantMethod = FormantCeilingMethod.Legacy };

    /// <summary>A take as v0.3.0 saved it: analysed at 5500/5000 Hz, no method in take.json.</summary>
    private async Task<(RecordingStore Store, Recording Take)> OldTake()
    {
        var store = new RecordingStore(new DataPaths(Path.Combine(_root, "data")));
        var result = await Legacy.AnalyzeAsync(Wav);
        Assert.Null(result.Metrics.Formants.Method);
        Assert.Null(result.Metrics.Formants.CeilingHz);
        var take = store.Add(result.Metrics, result.Detail, Wav, result.Series);
        return (store, store.Rename(take, "an old take"));
    }

    private static Dictionary<string, byte[]> Files(string folder) =>
        Directory.GetFiles(folder).ToDictionary(Path.GetFileName, File.ReadAllBytes)!;

    [Fact]
    public async Task AnOldTakeIsReanalysedBesideItsOwnFilesWhichStayAsTheyWere()
    {
        var (store, take) = await OldTake();
        var before = Files(store.FolderOf(take));
        var reanalysis = new TakeReanalysis(store, new AcousticsAnalysisEngine());
        Assert.Equal(FormantCeilingMethod.Legacy, FormantCeilings.MethodOf(take));
        Assert.True(reanalysis.Needs(take));

        var result = await reanalysis.ReanalyseAsync(take);

        Assert.NotNull(result);
        var after = Files(store.FolderOf(take));
        foreach (var (name, bytes) in before) Assert.Equal(bytes, after[name]); // take.json, detail.json, series.json, take.wav untouched
        Assert.Equal([RecordingStore.ReanalysedDetailJson, RecordingStore.ReanalysedSeriesJson, RecordingStore.ReanalysedTakeJson],
            after.Keys.Except(before.Keys).Order(StringComparer.Ordinal));
        Assert.False(reanalysis.Needs(take));

        var kept = store.LoadReanalysis(take)!;
        Assert.Equal(Formants.PathMethod, kept.Metrics.Formants.Method);
        Assert.Equal(take.Id, kept.Metrics.Id);
        Assert.Equal("an old take", kept.Metrics.Label);
        Assert.NotNull(kept.Metrics.Formants.CeilingHz);
        Assert.NotNull(kept.Series);
        Assert.Equal((await new AcousticsAnalysisEngine().AnalyzeAsync(Wav)).Metrics.Formants.F2Hz, kept.Metrics.Formants.F2Hz);

        // The store still lists one take, with its own (old) numbers.
        var listed = Assert.Single(store.Load());
        Assert.Null(listed.Formants.Method);
        Assert.Equal(take.Formants.F2Hz, listed.Formants.F2Hz);
    }

    [Fact]
    public async Task ATakeAnalysedTodayNeedsNoReanalysis()
    {
        var store = new RecordingStore(new DataPaths(Path.Combine(_root, "today")));
        var result = await new AcousticsAnalysisEngine().AnalyzeAsync(Wav);
        var take = store.Add(result.Metrics, result.Detail, Wav, result.Series);
        Assert.Equal(FormantCeilingMethod.Path, FormantCeilings.MethodOf(take));
        Assert.False(new TakeReanalysis(store, new AcousticsAnalysisEngine()).Needs(take));
        Assert.Null(await new TakeReanalysis(store, new AcousticsAnalysisEngine()).ReanalyseAsync(take));
    }

    [Fact]
    public async Task TheEngineCanMeasureAsAnOldTakeWas()
    {
        // The trend backfill re-analyses a take with the method of its own numbers.
        IAnalysisEngine engine = new AcousticsAnalysisEngine();
        var legacy = await engine.WithFormantMethod(FormantCeilingMethod.Legacy).AnalyzeAsync(Wav);
        Assert.Equal((await Legacy.AnalyzeAsync(Wav)).Metrics.Formants.F2Hz, legacy.Metrics.Formants.F2Hz);
        Assert.Same(engine, engine.WithFormantMethod(FormantCeilingMethod.Path));
    }

    [Fact]
    public async Task AnOldTakesArchiveStillImportsWithItsNumbers()
    {
        var (store, take) = await OldTake();
        var archive = Path.Combine(_root, "old.tmbr");
        await using (var file = File.Create(archive))
            TakeArchive.Write(file, take, store.LoadDetail(take)!, store.LoadSeries(take)!, store.Paths.Resolve(take.Audio)!);

        var target = new RecordingStore(new DataPaths(Path.Combine(_root, "other")));
        var imported = await new TakeImportService(target, new AcousticsAnalysisEngine(),
            new Audio.Decoding.DecoderRegistry([new Audio.Decoding.ManagedDecoderProvider()])).ImportAsync(archive);
        Assert.Null(imported.Formants.Method);
        Assert.Equal(take.Formants.F2Hz, imported.Formants.F2Hz);
        Assert.True(new TakeReanalysis(target, new AcousticsAnalysisEngine()).Needs(imported));
    }
}
