using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Tests;

/// <summary>The shared take state: the catalog and which take is shown.</summary>
public sealed class TakeStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private RecordingStore StoreWith(int takes)
    {
        var store = new RecordingStore(new DataPaths(_root));
        var wav = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Reyfen.Timbratune.Core.Tests", "Fixtures", "vctk_f294.wav");
        for (var i = 0; i < takes; i++) store.Add(new Recording { Label = $"take {i + 1}" }, new RecordingDetail(), wav);
        return store;
    }

    [Fact]
    public async Task TheNewestIsShownUntilAnotherIsPicked()
    {
        var catalog = new TakeCatalog(StoreWith(3));
        var active = new ActiveTakeService(catalog);
        var changes = 0;
        active.Changed += (_, _) => changes++;
        await catalog.ReloadAsync();
        Assert.Equal([1, 2, 3], catalog.Takes.Select(t => t.Id));
        Assert.Equal(3, active.Active?.Id);
        Assert.True(active.IsLatest);
        Assert.Equal(1, changes); // a reload always announces: the take's files may have changed

        active.Select(1);
        Assert.Equal(1, active.Active?.Id);
        Assert.False(active.IsLatest);
        active.Select(1); // no change, no announcement
        Assert.Equal(2, changes);

        active.SelectLatest();
        Assert.Equal(3, active.Active?.Id);
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task APickThatDisappearsFallsBackToTheNewest()
    {
        var store = StoreWith(3);
        var catalog = new TakeCatalog(store);
        var active = new ActiveTakeService(catalog);
        await catalog.ReloadAsync();
        active.Select(2);
        store.Delete(2);
        await catalog.ReloadAsync();
        Assert.Equal(3, active.Active?.Id);
        Assert.Null(catalog.LoadError);
    }

    [Fact]
    public async Task AnEmptyFolderHasNoActiveTake()
    {
        var catalog = new TakeCatalog(new RecordingStore(new DataPaths(_root)));
        var active = new ActiveTakeService(catalog);
        await catalog.ReloadAsync();
        Assert.Empty(catalog.Takes);
        Assert.Null(catalog.Latest);
        Assert.Null(active.Active);
        Assert.False(active.IsLatest);
    }
}
