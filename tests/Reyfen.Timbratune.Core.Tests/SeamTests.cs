using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>The seams of REFACTOR-PLAN.md §7: storage ports, stream entry points, the scheduler.</summary>
public sealed class SeamTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-seams-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".wav");

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void TakeStorageRefusesNamesThatAreNotOneSegment(string name)
    {
        var storage = new FileSystemTakeStorage(_root);
        Assert.Throws<ArgumentException>(() => storage.CreateTake(name));
        Assert.Throws<ArgumentException>(() => storage.Exists("001 a", name));
        Assert.Throws<ArgumentException>(() => storage.DeleteTake(name));
    }

    [Fact]
    public void TakeStorageMakesNamesUniqueAndRenamesOnlyOntoFreeNames()
    {
        var storage = new FileSystemTakeStorage(_root);
        Assert.Equal("001 a", storage.CreateTake("001 a"));
        Assert.Equal("001 a (2)", storage.CreateTake("001 a"));
        Assert.False(storage.TryRenameTake("001 a", "001 a (2)"));
        Assert.True(storage.TryRenameTake("001 a", "001 b"));
        Assert.Equal(["001 a (2)", "001 b"], storage.ListTakes().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AFailedWriteKeepsThePreviousFile()
    {
        var store = new FileSystemDataStore(_root);
        store.Write("settings/a.json", s => s.Write("old"u8));
        Assert.Throws<InvalidOperationException>(() => store.Write("settings/a.json", s =>
        {
            s.Write("new, half"u8);
            throw new InvalidOperationException("killed mid-write");
        }));
        using (var reader = new StreamReader(store.OpenRead("settings/a.json"))) Assert.Equal("old", reader.ReadToEnd());
        store.Move("settings/a.json", "settings/a.json.bak");
        Assert.False(store.Exists("settings/a.json"));
        Assert.True(store.Exists("settings/a.json.bak"));
        Assert.Throws<ArgumentException>(() => store.Write("../escape.json", _ => { }));
    }

    [Fact]
    public async Task StreamAndSoundEntryPointsGiveThePathsResult()
    {
        var engine = new AcousticsAnalysisEngine();
        var fromPath = await engine.AnalyzeAsync(Fixture("vctk_f339"));
        AnalysisResult fromStream;
        await using (var stream = File.OpenRead(Fixture("vctk_f339"))) fromStream = await engine.AnalyzeAsync(stream);
        Sound sound;
        await using (var stream = File.OpenRead(Fixture("vctk_f339"))) sound = WavDecoder.Decode(stream);
        var fromSound = await engine.AnalyzeAsync(sound);

        var expected = TimbratuneJson.WriteRecordings([fromPath.Metrics]) + TimbratuneJson.WriteDetail(fromPath.Detail);
        Assert.Equal(expected, TimbratuneJson.WriteRecordings([fromStream.Metrics]) + TimbratuneJson.WriteDetail(fromStream.Detail));
        Assert.Equal(expected, TimbratuneJson.WriteRecordings([fromSound.Metrics]) + TimbratuneJson.WriteDetail(fromSound.Detail));
    }

    [Fact]
    public async Task TheEnginesStreamDefaultWorksForPathOnlyEngines()
    {
        IAnalysisEngine engine = new PathOnlyEngine();
        await using var stream = File.OpenRead(Fixture("vctk_f294"));
        var result = await engine.AnalyzeAsync(stream);
        Assert.Equal(9.81, result.Metrics.DurationS);
    }

    [Fact]
    public void WavWriterOnAStreamWritesWhatTheFileVersionWrites()
    {
        var samples = Enumerable.Range(0, 5000).Select(i => (float)Math.Sin(i * 0.03) * 0.4f).ToArray();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "a.wav");
        using (var w = new WavWriter(path, 44100)) w.Write(samples);
        WavWriter.Truncate(path, 4321);

        using var memory = new MemoryStream();
        using (var w = new WavWriter(memory, 44100, leaveOpen: true)) w.Write(samples);
        WavWriter.Truncate(memory, 4321);
        Assert.Equal(File.ReadAllBytes(path), memory.ToArray());
        Assert.Throws<InvalidOperationException>(() => new WavWriter(new MemoryStream(), 44100).Path);
    }

    [Fact]
    public async Task LiveWorkRunsInOrderOnOneLimitedThread()
    {
        using var scheduler = new NativeAnalysisScheduler(2, null);
        var threads = new List<int>();
        var limits = new List<int?>();
        var order = new List<int>();
        var tasks = Enumerable.Range(0, 20).Select(i => scheduler.RunLiveAsync(() =>
        {
            lock (order)
            {
                order.Add(i);
                threads.Add(Environment.CurrentManagedThreadId);
                limits.Add(Parallelism.Options.MaxDegreeOfParallelism);
            }
            return i;
        })).ToList();
        await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(0, 20), order);
        Assert.Single(threads.Distinct());
        Assert.All(limits, l => Assert.Equal(2, l));
        Assert.Equal(-1, (await NativeAnalysisScheduler.Default.RunLiveAsync(() => Parallelism.Options.MaxDegreeOfParallelism)));
    }

    /// <summary>An engine that only reads files, like the Praat reference.</summary>
    private sealed class PathOnlyEngine : IAnalysisEngine
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
            new AcousticsAnalysisEngine().AnalyzeAsync(wavPath, registerFloorHz, cancellationToken, progress);
    }
}
