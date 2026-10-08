using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Diagnostics;
using Reyfen.Timbratune.Services;

namespace Reyfen.Timbratune.ViewModels;

/// <summary>App.tsx: the single scrolling dashboard.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HashSet<int> _backfilling = [];
    private readonly TakeImporter _importer;
    private List<Recording> _recordings = [];
    private IReadOnlyList<ReferenceVoice> _references = [];

    public MainViewModel(AppServices services)
    {
        _services = services;
        Record = new RecordViewModel(services.Recorder, services.Engine, services.Store, () => ReloadAsync(selectLatest: true),
            services.RequestMicrophone, services.LowerThreadPriority);
        OpenMetricCommand = new RelayCommand<MetricKey>(OpenMetric);
        _importer = new TakeImporter(services.Store, services.Engine, services.Decoder);
        // An import's note is about the takes list; a new recording makes it old news.
        Record.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordViewModel.IsRecording) && Record.IsRecording && !IsImporting) ImportStatus = null;
        };
        if (Features.ReferenceVoices && services.ReferenceDir is { } referenceDir) _references = LoadReferences(referenceDir);
    }

    public RecordViewModel Record { get; }
    public PlaybackService Playback => _services.Playback;
    public IRelayCommand<MetricKey> OpenMetricCommand { get; }

    /// <summary>Newest first, for the switcher and "All recordings".</summary>
    public ObservableCollection<RecordingItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    private TakeViewModel? _active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    private MetricModalViewModel? _modal;

    [ObservableProperty] private string? _loadError;

    private int? _selectedId;

    public bool HasActive => Active is not null;
    public bool IsModalOpen => Modal is not null;
    public bool ShowSwitcher => Items.Count > 1;
    public bool NoRecordings => Items.Count == 0;
    /// <summary>The takes folder (one folder per take; files dropped in are imported on the next start).</summary>
    public string DataFolder => _services.Store.Paths.TakesDir;

    /// <summary>Where the folder can't be opened from the app (Android): where to find it instead.</summary>
    public string? DataFolderHint => _services.DataFolderHint;
    public bool CanOpenDataFolder => DataFolderHint is null;

    /// <summary>What an import is doing, or how it went; null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImportStatus))] private string? _importStatus;
    [ObservableProperty] private bool _isImporting;
    public bool HasImportStatus => ImportStatus is not null;

    /// <summary>First load: the takes, then any audio or .tmbr files dropped into the takes folder.</summary>
    public async Task StartAsync()
    {
        await ReloadAsync(selectLatest: true);
        await Task.Run(_importer.CleanUp);
        var dropped = await Task.Run(_importer.FindDropped);
        if (dropped.Count == 0) return;
        await RunImportsAsync(dropped.Select(path => (Path.GetFileName(path),
            (Func<IProgress<double>, Task<Recording>>)(progress => _importer.ImportDroppedAsync(path, progress)))).ToList(),
            "found in the takes folder");
    }

    [RelayCommand]
    private async Task OpenDataFolderAsync()
    {
        if (!await _services.Dialogs.OpenFolderAsync(DataFolder)) ImportStatus = $"couldn't open the folder here · it's {DataFolder}";
    }

    /// <summary>Import button: audio files (analyzed) and .tmbr files (taken as they are).</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsImporting) return;
        var files = await _services.Dialogs.OpenFilesAsync("Import takes", "Audio or Timbratune takes", _importer.Extensions);
        var usable = files.Where(f => _importer.CanImport(f.Name)).ToList();
        if (files.Count > 0 && usable.Count == 0)
        {
            ImportStatus = $"can't import that · use {string.Join(", ", _importer.Extensions.Select(e => "." + e))}";
            return;
        }
        await RunImportsAsync(usable.Select(f => (f.Name, (Func<IProgress<double>, Task<Recording>>)(progress => ImportPickedAsync(f, progress))))
            .ToList(), null);
    }

    // A picked file may have no path (Android): copied to a temporary file first, then imported.
    private async Task<Recording> ImportPickedAsync(PickedFile file, IProgress<double> progress)
    {
        var temp = Path.Combine(_services.Store.Paths.Root, ".import", "picked-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            await using (var source = await file.OpenRead())
            await using (var target = File.Create(temp))
                await source.CopyToAsync(target);
            return await _importer.ImportAsync(temp, file.Name, progress);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private async Task RunImportsAsync(IReadOnlyList<(string Name, Func<IProgress<double>, Task<Recording>> Import)> imports, string? where)
    {
        if (imports.Count == 0) return;
        IsImporting = true;
        var failed = new List<string>();
        var done = 0;
        try
        {
            for (var i = 0; i < imports.Count; i++)
            {
                var (name, import) = imports[i];
                var prefix = imports.Count > 1 ? $"importing {i + 1} of {imports.Count}" : "importing";
                ImportStatus = $"{prefix} · {name}…";
                var progress = new ThrottledProgress(f =>
                {
                    if (IsImporting) ImportStatus = $"{prefix} · {name} · {f:P0}"; // late reports after the end are dropped
                });
                try
                {
                    await import(progress);
                    done++;
                    await ReloadAsync(selectLatest: true);
                }
                catch (Exception ex)
                {
                    failed.Add($"{name} ({ex.Message})");
                }
            }
        }
        finally
        {
            IsImporting = false;
        }
        var summary = done == 0 ? "" : $"imported {done} {(done == 1 ? "take" : "takes")}{(where is null ? "" : " " + where)} ✨";
        if (failed.Count > 0) summary += (summary.Length > 0 ? " · " : "") + "couldn't import " + string.Join(", ", failed) + " 🌧️";
        ImportStatus = summary;
    }

    public async Task ReloadAsync(bool selectLatest = false)
    {
        try
        {
            _recordings = await Task.Run(_services.Store.Load);
            LoadError = _services.Store.Unreadable.Count == 0
                ? null
                : $"couldn't read {string.Join(", ", _services.Store.Unreadable)} in the takes folder 🌧️";
        }
        catch (Exception ex)
        {
            LoadError = $"couldn't load recordings 🌧️ {ex.Message}";
            _recordings = [];
        }
        if (selectLatest) _selectedId = null;
        if (_selectedId is { } id && _recordings.All(r => r.Id != id)) _selectedId = null;

        foreach (var item in Items) item.Dispose();
        Items.Clear();
        var latestId = _recordings.Count > 0 ? _recordings[^1].Id : (int?)null;
        for (var i = _recordings.Count - 1; i >= 0; i--)
        {
            var r = _recordings[i];
            Items.Add(new RecordingItemViewModel(r, _services.Store.Paths.Resolve(r.Audio), r.Id == latestId,
                _services.Playback, _services.Dialogs, DeleteAsync, ExportDataAsync, RenameAsync));
        }

        OnPropertyChanged(nameof(ShowSwitcher));
        OnPropertyChanged(nameof(NoRecordings));
        UpdateActive();
    }

    [RelayCommand]
    private void Select(RecordingItemViewModel item)
    {
        _selectedId = item.Id;
        UpdateActive();
    }

    [RelayCommand]
    private void CloseModal()
    {
        Modal?.CloseCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
    }

    /// <summary>
    /// Exports a take as a .tmbr file (WAV + take.json, see <see cref="TakeArchive"/>).
    /// Takes analyzed before the per-frame lists were kept get them now: the stored WAV is
    /// analyzed once and the lists saved for next time. The lists are only held while the
    /// file is written. Returns where it went, or null if cancelled.
    /// </summary>
    private async Task<string?> ExportDataAsync(Recording take, string audioPath)
    {
        var store = _services.Store;
        var (detail, series) = await Task.Run(() => (store.LoadDetail(take), store.LoadSeries(take)));
        if (detail is null) throw new InvalidOperationException("this take's analysis file is missing");
        if (series is null)
        {
            var floor = take.Register?.FloorHz ?? Core.Analysis.AnalysisPostProcessor.DefaultRegisterFloorHz;
            var result = await _services.Engine.AnalyzeAsync(audioPath, floor);
            series = result.Series ?? throw new InvalidOperationException("the analysis gave no per-frame lists");
            detail = result.Detail;
            var analyzed = series;
            await Task.Run(() =>
            {
                store.SaveDetail(take, detail);
                store.SaveSeries(take, analyzed);
            });
            if (Active?.Recording.Id == take.Id) UpdateActive();
        }

        var (d, s) = (detail, series);
        return await _services.Dialogs.SaveAsync("Export this take's data", $"voice-take-{take.Id}.{TakeArchive.Extension}",
            "Timbratune take", TakeArchive.Extension, "application/octet-stream",
            target => Task.Run(() => TakeArchive.Write(target, take, d, s, audioPath, Features.Version)));
    }

    /// <summary>Renames a take (label and folder) and shows the new name everywhere.</summary>
    private async Task RenameAsync(Recording take, string label)
    {
        // A take that is playing keeps its folder name: the player holds its audio open.
        var playing = _services.Store.Paths.Resolve(take.Audio) is { } audio && _services.Playback.CurrentPath == audio;
        await Task.Run(() => _services.Store.Rename(take, label, renameFolder: !playing));
        await ReloadAsync();
    }

    private async Task DeleteAsync(int id)
    {
        await Task.Run(() => _services.Store.Delete(id));
        if (_selectedId == id) _selectedId = null;
        await ReloadAsync();
    }

    private void UpdateActive()
    {
        var latest = _recordings.Count > 0 ? _recordings[^1] : null;
        var active = (_selectedId is { } id ? _recordings.FirstOrDefault(r => r.Id == id) : null) ?? latest;
        foreach (var item in Items) item.IsActive = active is not null && item.Id == active.Id;
        if (active is null)
        {
            Active = null;
            return;
        }
        RecordingDetail? detail;
        using (Perf.Measure("take.load")) detail = _services.Store.LoadDetail(active);
        using (Perf.Measure("take.viewmodel")) Active = new TakeViewModel(active, detail, active.Id == latest?.Id, OpenMetricCommand);
        if (detail is not null && (detail.Trends?.Version ?? 0) < TakeTrends.CurrentVersion) _ = BackfillTrendsAsync(active);
    }

    /// <summary>
    /// Takes analyzed before the time trends existed have only the pitch-based
    /// trends (rebuilt from the contour). Re-run the analysis on the stored WAV once, save the
    /// fuller detail file, and refresh the view if that take is still shown.
    /// </summary>
    private async Task BackfillTrendsAsync(Recording take)
    {
        var audio = _services.Store.Paths.Resolve(take.Audio);
        if (!_services.Engine.IsAvailable || audio is null || !File.Exists(audio) || !_backfilling.Add(take.Id)) return;
        try
        {
            var floor = take.Register?.FloorHz ?? Core.Analysis.AnalysisPostProcessor.DefaultRegisterFloorHz;
            var result = await _services.Engine.AnalyzeAsync(audio, floor);
            await Task.Run(() =>
            {
                _services.Store.SaveDetail(take, result.Detail);
                if (result.Series is { } series) _services.Store.SaveSeries(take, series);
            });
            if (Active?.Recording.Id == take.Id) UpdateActive();
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

    private void OpenMetric(MetricKey key)
    {
        static string? Existing(string? path) => path is not null && File.Exists(path) ? path : null;

        Modal?.Dispose();
        Modal = new MetricModalViewModel(Metrics.All[key], _recordings, _references, Active?.Recording.Id,
            r => Existing(_services.Store.Paths.Resolve(r.Audio)),
            v => v.Audio is { } a && _services.ReferenceDir is { } dir ? Existing(Path.Combine(dir, a)) : null,
            _services.Playback,
            () =>
            {
                Modal?.Dispose();
                Modal = null;
            });
    }

    private static IReadOnlyList<ReferenceVoice> LoadReferences(string dir)
    {
        // Missing or broken reference.json just means no reference ticks.
        try
        {
            var path = Path.Combine(dir, "reference.json");
            return File.Exists(path) ? TimbratuneJson.ReadReferences(File.ReadAllText(path, Encoding.UTF8)) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
