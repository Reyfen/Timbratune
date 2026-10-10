using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>Importing audio and .tmbr files, from the Import button and from the takes folder.</summary>
public sealed class ImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-import-" + Guid.NewGuid().ToString("N"));

    public ImportTests() => Directory.CreateDirectory(_root);
    private static readonly string Wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vctk_f339.wav");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Stands in for the SoundFlow decoder: "decodes" any file to the fixture WAV.</summary>
    private sealed class FakeDecoder : IAudioDecoder
    {
        public void DecodeToWav(string sourcePath, string wavPath) => File.Copy(Wav, wavPath);
    }

    private RecordingStore Store(string name = "data") => new(new DataPaths(Path.Combine(_root, name)));

    [Fact]
    public async Task AudioIsAnalyzedAndNamedAfterTheFile()
    {
        var store = Store();
        var source = Path.Combine(_root, "my voice.wav");
        File.Copy(Wav, source);
        var take = await new TakeImporter(store, new AcousticsAnalysisEngine(), null).ImportAsync(source);

        Assert.Equal("my voice", take.Label);
        Assert.Equal("my voice.wav", take.SourceFile);
        Assert.NotNull(take.Pitch.MeanHz);
        Assert.True(store.HasSeries(take));
        Assert.True(File.Exists(source)); // the Import button leaves the original where it was
        Assert.Equal(File.ReadAllBytes(Wav), File.ReadAllBytes(store.Paths.Resolve(take.Audio)!));
    }

    [Fact]
    public async Task ATmbrFileComesBackAsTheSameTakeWithoutAnalysis()
    {
        var engine = new AcousticsAnalysisEngine();
        var from = Store("from");
        var result = await engine.AnalyzeAsync(Wav);
        result.Metrics.Label = "shared take";
        result.Metrics.Note = "from a friend";
        result.Metrics.Date = "2026-09-30";
        var original = from.Add(result.Metrics, result.Detail, Wav, result.Series);
        var tmbr = Path.Combine(_root, "shared.tmbr");
        using (var file = File.Create(tmbr))
            TakeArchive.Write(file, original, from.LoadDetail(original)!, from.LoadSeries(original)!, from.Paths.Resolve(original.Audio)!);

        var to = Store("to");
        to.Add(new Recording { Label = "already here" }, new RecordingDetail(), Wav);
        // No decoder and an engine that must not run: .tmbr files aren't analyzed.
        var take = await new TakeImporter(to, new ThrowingEngine(), null).ImportAsync(tmbr);

        Assert.Equal(2, take.Id);
        Assert.Equal(("shared take", "from a friend", "2026-09-30"), (take.Label, take.Note, take.Date));
        Assert.Equal(original.Pitch.MeanHz, take.Pitch.MeanHz);
        Assert.Equal(original.Weight?.H1a3cDb, take.Weight?.H1a3cDb);
        Assert.Equal(from.LoadDetail(original)!.Frames.Hz, to.LoadDetail(take)!.Frames.Hz);
        Assert.Equal(from.LoadSeries(original)!.Series[TakeSeries.Weight].Values, to.LoadSeries(take)!.Series[TakeSeries.Weight].Values);
        Assert.Equal(File.ReadAllBytes(Wav), File.ReadAllBytes(to.Paths.Resolve(take.Audio)!));
        Assert.Equal(["already here", "shared take"], to.Load().Select(r => r.Label));
    }

    [Fact]
    public async Task FilesDroppedIntoTheTakesFolderAreImportedAndTidiedAway()
    {
        var store = Store();
        Directory.CreateDirectory(store.Paths.TakesDir);
        File.Copy(Wav, Path.Combine(store.Paths.TakesDir, "dropped.wav"));
        File.WriteAllText(Path.Combine(store.Paths.TakesDir, "song.mp3"), "not really an mp3");
        File.WriteAllText(Path.Combine(store.Paths.TakesDir, "readme.txt"), "ignored");

        var importer = new TakeImporter(store, new AcousticsAnalysisEngine(), new FakeDecoder());
        var dropped = importer.FindDropped();
        Assert.Equal(["dropped.wav", "song.mp3"], dropped.Select(Path.GetFileName));
        foreach (var path in dropped) await importer.ImportDroppedAsync(path);

        Assert.Empty(importer.FindDropped());
        var takes = store.Load();
        Assert.Equal(["dropped", "song"], takes.Select(r => r.Label));
        Assert.False(File.Exists(Path.Combine(store.Paths.TakesDir, "dropped.wav"))); // now the take's take.wav
        Assert.True(File.Exists(Path.Combine(store.FolderOf(takes[1]), "source.mp3"))); // the original kept with its take
        Assert.True(File.Exists(Path.Combine(store.Paths.TakesDir, "readme.txt")));
    }

    [Fact]
    public async Task WithoutADecoderOnlyWavAndTmbrAreAccepted()
    {
        var importer = new TakeImporter(Store(), new AcousticsAnalysisEngine(), null);
        Assert.Equal(["wav", "tmbr"], importer.Extensions);
        Assert.False(importer.CanImport("a.mp3"));
        Assert.True(importer.CanImport("a.TMBR"));

        var notWav = Path.Combine(_root, "fake.wav");
        File.WriteAllText(notWav, "not audio");
        await Assert.ThrowsAsync<NotSupportedException>(() => importer.ImportAsync(notWav));
        Assert.Empty(Store().Load()); // nothing half-made is left
    }

    private sealed class ThrowingEngine : IAnalysisEngine
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
            throw new InvalidOperationException("a .tmbr import must not analyze");
    }
}
