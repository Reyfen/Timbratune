using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio.Decoding;
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

    private static string Format(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats", name);

    /// <summary>Stands in for miniaudio: "decodes" MP3 to the fixture's samples.</summary>
    private sealed class FakeMp3Provider : IAudioDecoderProvider
    {
        public string Id => "fake";
        public int Priority => 2;
        public ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default) => new(new FormatSet([AudioFormatCatalog.Mp3]));
        public IAudioDecoder Create() => new ManagedDecoderProvider().Create() is var wav ? new Redirect(wav) : null!;

        private sealed class Redirect(IAudioDecoder wav) : IAudioDecoder
        {
            public Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken) =>
                wav.DecodeAsync(AudioSource.FromFile(Wav), sink, progress, cancellationToken);
            public void Dispose() => wav.Dispose();
        }
    }

    private static DecoderRegistry Decoders(bool mp3 = false) =>
        new(mp3 ? [new ManagedDecoderProvider(), new FakeMp3Provider()] : [new ManagedDecoderProvider()]);

    private RecordingStore Store(string name = "data") => new(new DataPaths(Path.Combine(_root, name)));

    [Fact]
    public async Task AudioIsAnalyzedAndNamedAfterTheFile()
    {
        var store = Store();
        var source = Path.Combine(_root, "my voice.wav");
        File.Copy(Wav, source);
        var take = await new TakeImportService(store, new AcousticsAnalysisEngine(), Decoders()).ImportAsync(source);

        Assert.Equal("my voice", take.Label);
        Assert.Equal("my voice.wav", take.SourceFile);
        Assert.NotNull(take.Pitch.MeanHz);
        Assert.True(store.HasSeries(take));
        Assert.True(File.Exists(source)); // the Import button leaves the original where it was
        Assert.Equal(File.ReadAllBytes(Wav), File.ReadAllBytes(store.Paths.Resolve(take.Audio)!)); // a readable WAV is kept as it is
        Assert.Equal(["detail.json", "series.json", "take.json", "take.wav"], Directory.GetFiles(store.FolderOf(take)).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task OtherFormatsBecomeAnAnalysisWavAndKeepTheirOriginal()
    {
        var store = Store();
        var source = Path.Combine(_root, "from my phone.mp3"); // an AIFF in disguise: the content decides
        File.Copy(Format("voice-22k-stereo.aiff"), source);
        var take = await new TakeImportService(store, new AcousticsAnalysisEngine(), Decoders()).ImportAsync(source);

        Assert.Equal("from my phone", take.Label);
        Assert.True(File.Exists(source));
        var folder = store.FolderOf(take);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(folder, "source.aiff")));
        using var wav = File.OpenRead(Path.Combine(folder, "take.wav"));
        var sound = Acoustics.WavDecoder.Decode(wav);
        Assert.Equal((44100.0, 1), (sound.SamplingFrequency, sound.ChannelCount));
        Assert.InRange(sound.Duration, 0.499, 0.501);
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
        var take = await new TakeImportService(to, new ThrowingEngine(), Decoders()).ImportAsync(tmbr);

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
        File.Copy(Format("voice-id3.mp3"), Path.Combine(store.Paths.TakesDir, "song.mp3"));
        File.WriteAllText(Path.Combine(store.Paths.TakesDir, "readme.txt"), "ignored");
        File.Copy(Format("voice.ogg"), Path.Combine(store.Paths.TakesDir, "no decoder here.ogg"));

        var importer = new TakeImportService(store, new AcousticsAnalysisEngine(), Decoders(mp3: true));
        var dropped = await importer.FindDroppedAsync();
        Assert.Equal(["dropped.wav", "song.mp3"], dropped.Select(Path.GetFileName));
        foreach (var path in dropped) await importer.ImportDroppedAsync(path);

        Assert.Empty(await importer.FindDroppedAsync());
        var takes = store.Load();
        Assert.Equal(["dropped", "song"], takes.Select(r => r.Label));
        Assert.False(File.Exists(Path.Combine(store.Paths.TakesDir, "dropped.wav"))); // now the take's take.wav
        Assert.True(File.Exists(Path.Combine(store.FolderOf(takes[1]), "source.mp3"))); // the original kept with its take
        Assert.True(File.Exists(Path.Combine(store.Paths.TakesDir, "readme.txt")));
        Assert.True(File.Exists(Path.Combine(store.Paths.TakesDir, "no decoder here.ogg"))); // left alone
    }

    [Fact]
    public async Task OnlyFormatsADecoderHereReadsAreAccepted()
    {
        var importer = new TakeImportService(Store(), new AcousticsAnalysisEngine(), Decoders());
        Assert.Equal(["wav", "wave", "aiff", "aif", "aifc", "tmbr"], await importer.GetExtensionsAsync());
        Assert.False(await importer.CanImportAsync("a.mp3"));
        Assert.True(await importer.CanImportAsync("a.AIF"));
        Assert.True(await importer.CanImportAsync("a.TMBR"));

        var notAudio = Path.Combine(_root, "fake.wav");
        File.WriteAllText(notAudio, "not audio");
        var unknown = await Assert.ThrowsAsync<UnsupportedAudioException>(() => importer.ImportAsync(notAudio));
        Assert.Null(unknown.Format);

        var mp3 = Path.Combine(_root, "song.mp3");
        File.Copy(Format("voice.mp3"), mp3);
        var noDecoder = await Assert.ThrowsAsync<UnsupportedAudioException>(() => importer.ImportAsync(mp3));
        Assert.Equal(AudioFormatCatalog.Mp3, noDecoder.Format?.Id);
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
