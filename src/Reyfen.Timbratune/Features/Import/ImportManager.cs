using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;
using Reyfen.Timbratune.Shared.Telemetry;

namespace Reyfen.Timbratune.Features.Import;

/// <summary>
/// Brings takes in: files picked with the Import button, files dropped into the takes folder
/// (imported at start), and the trend backfill for takes analyzed before the time trends
/// existed. Each new take reloads the takes and becomes the one shown.
/// </summary>
public sealed class ImportManager
{
    private readonly TakeImportService _importer;
    private readonly RecordingStore _store;
    private readonly IAnalysisEngine _engine;
    private readonly ITakeCatalog _catalog;
    private readonly IActiveTakeService _active;
    private readonly IMessenger _messenger;
    private readonly ISettings _settings;
    private readonly IDecoderInstaller? _ffmpeg;
    private readonly IAnalytics _analytics;
    private readonly ICrashReporter _crashes;
    private readonly IAppLog _log;
    private readonly HashSet<int> _backfilling = [];

    public ImportManager(TakeImportService importer, RecordingStore store, IAnalysisEngine engine, ITakeCatalog catalog,
        IActiveTakeService active, IMessenger messenger, ISettings settings, IEnumerable<IDecoderInstaller> installers,
        IAnalytics? analytics = null, ICrashReporter? crashes = null, IAppLog? log = null)
    {
        _analytics = analytics ?? NullAnalytics.Instance;
        _crashes = crashes ?? NullCrashReporter.Instance;
        _log = log ?? NullAppLog.Instance;
        _settings = settings;
        _ffmpeg = installers.FirstOrDefault(i => i.ProviderId == "ffmpeg");
        _importer = importer;
        _store = store;
        _engine = engine;
        _catalog = catalog;
        _active = active;
        _messenger = messenger;
        messenger.Register<ImportManager, TakeTrendsOutdated>(this, (m, message) => _ = m.BackfillTrendsAsync(message.TakeId));
    }

    /// <summary>What the Import picker offers: the formats this device decodes (probed once, again after a decoder was installed), and .tmbr.</summary>
    public async Task<IReadOnlyList<FileTypeFilter>> GetPickerTypesAsync() =>
        ImportFilters.For(await _importer.Decoders.GetFormatsAsync());

    /// <summary>Removes leftovers of imports that were interrupted.</summary>
    public Task CleanUpAsync() => Task.Run(_importer.CleanUp);

    /// <summary>
    /// Files dropped into the takes folder: those that can be imported, and audio files nothing here
    /// decodes (left where they are).
    /// </summary>
    public Task<DroppedFiles> FindDroppedAsync() => Task.Run(async () =>
    {
        var importable = await _importer.FindDroppedAsync();
        var formats = await _importer.Decoders.GetFormatsAsync();
        var unsupported = Directory.Exists(_store.Paths.TakesDir)
            ? Directory.GetFiles(_store.Paths.TakesDir).Order(StringComparer.Ordinal)
                .Where(f => AudioFormatCatalog.ByExtension(f) is { } format && !formats.Contains(format.Id)).ToList()
            : [];
        return new DroppedFiles(importable, unsupported);
    });

    /// <summary>A file from a picker; it may have no path (Android), so it is copied to a temporary file first.</summary>
    public async Task<Recording> ImportPickedAsync(PickedFile file, IProgress<double> progress)
    {
        var temp = Path.Combine(_store.Paths.Root, ".import", "picked-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            return await ImportAsync("picked", async () =>
            {
                await using (var source = await file.OpenRead())
                await using (var target = File.Create(temp))
                    await source.CopyToAsync(target);
                return await _importer.ImportAsync(temp, file.Name, progress, moveOriginal: true);
            });
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>A file dropped into the takes folder; it is removed from there once imported.</summary>
    public Task<Recording> ImportDroppedAsync(string path, IProgress<double> progress) =>
        ImportAsync("dropped", () => _importer.ImportDroppedAsync(path, progress));

    /// <summary>One import, timed and logged; a failure (most often a file nothing here reads) is logged, not reported.</summary>
    private async Task<Recording> ImportAsync(string kind, Func<Task<Recording>> import)
    {
        var start = Stopwatch.GetTimestamp();
        Recording take;
        try
        {
            take = await import();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.Log(AppLogLevel.Warning, "import", e, $"a {kind} file failed");
            throw;
        }
        var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        _log.Log(AppLogLevel.Info, "import", $"a {kind} file imported in {seconds:0.0} s");
        if (_analytics.IsEnabled) _analytics.TakeAnalyzed(new(TakeSource.Imported, take, seconds));
        return await Added(take);
    }

    /// <summary>
    /// Whether to offer FFmpeg unasked: where the installer offers itself (Linux), a pinned build
    /// fits and no ffmpeg is there, and "not now" wasn't chosen before.
    /// </summary>
    public bool ShouldOfferFfmpeg => _ffmpeg is { IsOffered: true, CanInstall: true } && !_settings.Get(ImportSettings.FfmpegOfferDeclined);

    /// <summary>What the FFmpeg download is (for the offer); null without an installer.</summary>
    public DecoderDownload? FfmpegDownload => _ffmpeg?.Describe();

    public string? FfmpegPackageCommand => _ffmpeg?.PackageManagerCommand;

    public void DeclineFfmpeg() => _settings.Set(ImportSettings.FfmpegOfferDeclined, true);

    /// <summary>Downloads FFmpeg, then probes the decoders again, so the next picker offers its formats.</summary>
    public async Task InstallFfmpegAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (_ffmpeg is null) throw new InvalidOperationException("No FFmpeg installer here.");
        await Task.Run(() => _ffmpeg.InstallAsync(progress, cancellationToken), cancellationToken);
        _log.Log(AppLogLevel.Info, "import", "FFmpeg installed");
        await _importer.Decoders.RefreshAsync();
    }

    /// <summary>Probes the decoders again (e.g. after a package manager installed ffmpeg); true when ffmpeg is now there.</summary>
    public async Task<bool> RefreshDecodersAsync()
    {
        await _importer.Decoders.RefreshAsync();
        return (await _importer.Decoders.GetFormatsAsync("ffmpeg")).Count > 0;
    }

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
        catch (Exception e)
        {
            // Non-essential: the pitch-based trends are already shown.
            _log.Log(AppLogLevel.Warning, "import", e, "the trend backfill failed");
            if (_crashes.IsEnabled) _crashes.Report(e, "import.backfill");
        }
        finally
        {
            _backfilling.Remove(take.Id);
        }
    }
}

/// <param name="Importable">Files to import now.</param>
/// <param name="Unsupported">Audio files in a format no decoder here reads.</param>
public sealed record DroppedFiles(IReadOnlyList<string> Importable, IReadOnlyList<string> Unsupported);
