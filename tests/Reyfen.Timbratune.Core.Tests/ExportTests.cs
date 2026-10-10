using System.IO.Compression;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>The per-frame lists kept with each take and the .tmbr export.</summary>
public sealed class ExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-export-" + Guid.NewGuid().ToString("N"));
    private static readonly string Wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task AnalysisGivesEverySeriesOnItsFrameGrid()
    {
        var result = await new AcousticsAnalysisEngine().AnalyzeAsync(Wav);
        var series = Assert.IsType<TakeSeries>(result.Series);
        string[] names = [TakeSeries.Pitch, TakeSeries.Loudness, TakeSeries.Hnr, TakeSeries.F1, TakeSeries.F2, TakeSeries.F3,
            TakeSeries.Weight, TakeSeries.Jitter];
        Assert.Equal(names.Order(), series.Series.Keys.Order());
        foreach (var name in names)
        {
            var s = series.Series[name];
            Assert.Equal(s.T.Count, s.Values.Count);
            Assert.NotEmpty(s.T);
            Assert.True(s.T.Zip(s.T.Skip(1)).All(p => p.Second > p.First), $"{name}: times not increasing");
        }

        // The pitch list is the saved 10 ms contour, frame for frame.
        var pitch = series.Series[TakeSeries.Pitch];
        Assert.Equal(AcousticsAnalysisEngine.PitchStep, pitch.StepS!.Value, 12);
        Assert.Equal(result.Detail.Frames.T, pitch.T);
        Assert.Equal(result.Detail.Frames.Hz.Select(h => h is > 0 ? h : null), pitch.Values);
        Assert.Equal(AcousticsAnalysisEngine.IntensityStep, series.Series[TakeSeries.Loudness].StepS!.Value, 12);
        Assert.Null(series.Series[TakeSeries.Jitter].StepS);

        // Every voiced frame, not the ≤ 250 subsampled ones the weight metric is taken from.
        var voiced = pitch.Values.Count(v => v is not null);
        Assert.InRange(series.Series[TakeSeries.Weight].T.Count, 0.8 * voiced, voiced);
        Assert.True(series.Series[TakeSeries.F2].T.Count > 50);
        Assert.All(series.Series[TakeSeries.F1].Values, v => Assert.InRange(v!.Value, 250, 1000));

        // The lists agree with the take's own numbers.
        var meanHz = pitch.Values.Where(v => v is not null).Average(v => v!.Value);
        Assert.Equal(result.Metrics.Pitch.MeanHz!.Value, meanHz, 0.02 * meanHz);
        var hnr = series.Series[TakeSeries.Hnr].Values.Average(v => v!.Value);
        Assert.Equal(result.Metrics.VoiceQuality.HnrDb!.Value, hnr, 1.0);
    }

    [Fact]
    public async Task StoreKeepsAndDeletesTheSeriesWithTheTake()
    {
        var result = await new AcousticsAnalysisEngine().AnalyzeAsync(Wav);
        var store = new RecordingStore(new DataPaths(_root));
        var take = store.Add(result.Metrics, result.Detail, Wav, result.Series);

        Assert.True(store.HasSeries(take));
        var loaded = Assert.IsType<TakeSeries>(store.LoadSeries(take));
        Assert.Equal(result.Series!.Series[TakeSeries.Weight].Values, loaded.Series[TakeSeries.Weight].Values);

        var older = store.Add(new Recording(), new RecordingDetail(), Wav);
        Assert.False(store.HasSeries(older));
        Assert.Null(store.LoadSeries(older));

        var folder = store.FolderOf(take);
        Assert.True(File.Exists(Path.Combine(folder, RecordingStore.SeriesJson)));
        store.Delete(take.Id);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ArchiveHoldsTheWavAndEveryList()
    {
        var result = await new AcousticsAnalysisEngine().AnalyzeAsync(Wav);
        var store = new RecordingStore(new DataPaths(_root));
        var take = store.Add(result.Metrics, result.Detail, Wav, result.Series);
        take.Label = "export test";
        var audio = store.Paths.Resolve(take.Audio)!;

        using var file = new MemoryStream();
        TakeArchive.Write(file, take, store.LoadDetail(take)!, store.LoadSeries(take)!, audio, "9.8.7");

        file.Position = 0;
        using (var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal([TakeArchive.AudioEntry, TakeArchive.DataEntry], zip.Entries.Select(e => e.FullName));
            using var copy = new MemoryStream();
            using (var entry = zip.GetEntry(TakeArchive.AudioEntry)!.Open()) entry.CopyTo(copy);
            Assert.Equal(File.ReadAllBytes(Wav), copy.ToArray());
        }

        file.Position = 0;
        var export = TakeArchive.Read(file);
        Assert.Equal(TakeExport.FormatName, export.Format);
        Assert.Equal(TakeExport.CurrentExporterVersion, export.ExporterVersion);
        Assert.Equal("9.8.7", export.AppVersion);
        Assert.Equal("export test", export.Take.Label);
        Assert.Equal(TakeArchive.AudioEntry, export.Audio.File);
        Assert.True(export.Audio.SampleRate > 0);
        Assert.Equal(1, export.Audio.Channels);
        Assert.Equal(16, export.Audio.BitsPerSample);
        Assert.Equal(AcousticsAnalysisEngine.PitchStep, export.Analysis.PitchStepS, 12);
        Assert.Equal(AcousticsAnalysisEngine.FormantWindow / 4, export.Analysis.FormantStepS, 12);
        Assert.Equal(result.Metrics.Pitch.MeanHz, export.Metrics.Pitch.MeanHz);
        Assert.Null(export.Metrics.Audio);
        Assert.NotNull(export.Detail.Trends);
        Assert.Equal(export.Detail.Trends!.StepS, export.Analysis.TrendStepS);
        foreach (var (name, s) in result.Series!.Series)
        {
            Assert.Equal(s.T.Select(t => Math.Round(t, 3)), export.Series.Series[name].T);
            Assert.Equal(s.Values, export.Series.Series[name].Values);
        }
    }

    [Fact]
    public void ReadRefusesOtherZips()
    {
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true)) zip.CreateEntry("other.txt");
        file.Position = 0;
        Assert.Throws<InvalidDataException>(() => TakeArchive.Read(file));
    }
}
