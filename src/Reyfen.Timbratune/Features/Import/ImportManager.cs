using CommunityToolkit.Mvvm.Messaging;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.Import;

/// <summary>
/// Brings takes in: files picked with the Import button, files dropped into the takes folder
/// (imported at start), and the trend backfill for takes analyzed before the time trends
/// existed. Each new take reloads the takes and becomes the one shown.
/// </summary>
public sealed class ImportManager
{
    private readonly TakeImporter _importer;
    private readonly RecordingStore _store;
    private readonly IAnalysisEngine _engine;
    private readonly ITakeCatalog _catalog;
    private readonly IActiveTakeService _active;
    private readonly IMessenger _messenger;
    private readonly HashSet<int> _backfilling = [];

    public ImportManager(TakeImporter importer, RecordingStore store, IAnalysisEngine engine, ITakeCatalog catalog,
        IActiveTakeService active, IMessenger messenger)
    {
        _importer = importer;
        _store = store;
        _engine = engine;
        _catalog = catalog;
        _active = active;
        _messenger = messenger;
        messenger.Register<ImportManager, TakeTrendsOutdated>(this, (m, message) => _ = m.BackfillTrendsAsync(message.TakeId));
    }

    /// <summary>Extensions a picker offers: wav, mp3, flac (with a decoder), tmbr.</summary>
    public IReadOnlyList<string> Extensions => _importer.Extensions;

    public bool CanImport(string fileName) => _importer.CanImport(fileName);

    /// <summary>Removes leftovers of imports that were interrupted.</summary>
    public Task CleanUpAsync() => Task.Run(_importer.CleanUp);

    /// <summary>Files dropped into the takes folder that can be imported.</summary>
    public Task<IReadOnlyList<string>> FindDroppedAsync() => Task.Run(_importer.FindDropped);

    /// <summary>A file from a picker; it may have no path (Android), so it is copied to a temporary file first.</summary>
    public async Task<Recording> ImportPickedAsync(PickedFile file, IProgress<double> progress)
    {
        var temp = Path.Combine(_store.Paths.Root, ".import", "picked-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            await using (var source = await file.OpenRead())
            await using (var target = File.Create(temp))
                await source.CopyToAsync(target);
            return await Added(await _importer.ImportAsync(temp, file.Name, progress));
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>A file dropped into the takes folder; it is removed from there once imported.</summary>
    public async Task<Recording> ImportDroppedAsync(string path, IProgress<double> progress) =>
        await Added(await _importer.ImportDroppedAsync(path, progress));

    /// <summary>Announces how a batch of imports went.</summary>
    public void Finished(int imported, int failed) => _messenger.Send(new ImportFinished(imported, failed));

    private async Task<Recording> Added(Recording take)
    {
        _active.SelectLatest();
        await _catalog.ReloadAsync();
        return take;
    }

    /// <summary>
    /// Takes analyzed before the time trends existed have only the pitch-based trends (rebuilt
    /// from the contour). Re-run the analysis on the stored WAV once and save the fuller detail
    /// file; whoever shows the take reloads it.
    /// </summary>
    private async Task BackfillTrendsAsync(int takeId)
    {
        if (_catalog.Takes.FirstOrDefault(r => r.Id == takeId) is not { } take) return;
        var audio = _store.Paths.Resolve(take.Audio);
        if (!_engine.IsAvailable || audio is null || !File.Exists(audio) || !_backfilling.Add(take.Id)) return;
        try
        {
            var floor = take.Register?.FloorHz ?? AnalysisPostProcessor.DefaultRegisterFloorHz;
            var result = await _engine.AnalyzeAsync(audio, floor);
            await Task.Run(() =>
            {
                _store.SaveDetail(take, result.Detail);
                if (result.Series is { } series) _store.SaveSeries(take, series);
            });
            _messenger.Send(new TakeAnalysisUpdated(take.Id));
        }
        catch (Exception)
        {
            // Non-essential: the pitch-based trends are already shown.
        }
        finally
        {
            _backfilling.Remove(take.Id);
        }
    }
}
