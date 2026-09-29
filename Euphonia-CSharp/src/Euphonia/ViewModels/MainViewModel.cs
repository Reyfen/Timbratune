using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Euphonia.Core.Domain;
using Euphonia.Core.Json;
using Euphonia.Core.Models;
using Euphonia.Services;

namespace Euphonia.ViewModels;

/// <summary>App.tsx: the single scrolling dashboard.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HashSet<int> _backfilling = [];
    private List<Recording> _recordings = [];
    private IReadOnlyList<ReferenceVoice> _references = [];

    public MainViewModel(AppServices services)
    {
        _services = services;
        Record = new RecordViewModel(services.Recorder, services.Engine, services.Store, () => ReloadAsync(selectLatest: true));
        OpenMetricCommand = new RelayCommand<MetricKey>(OpenMetric);
        _references = LoadReferences(services.ReferenceDir);
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
    public string DataFolder => _services.Store.Paths.Root;

    public async Task ReloadAsync(bool selectLatest = false)
    {
        try
        {
            _recordings = await Task.Run(_services.Store.Load);
            LoadError = null;
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
                _services.Playback, _services.Dialogs, DeleteAsync));
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
        var detail = _services.Store.LoadDetail(active);
        Active = new TakeViewModel(active, detail, active.Id == latest?.Id, OpenMetricCommand);
        if (detail is { PhraseMetrics: null }) _ = BackfillPhraseMetricsAsync(active);
    }

    /// <summary>
    /// Takes analyzed before the per-phrase breakdown existed have no F2 /
    /// weight per phrase. Re-run the analysis on the stored WAV once, save the
    /// fuller detail file, and refresh the view if that take is still shown.
    /// </summary>
    private async Task BackfillPhraseMetricsAsync(Recording take)
    {
        var audio = _services.Store.Paths.Resolve(take.Audio);
        if (!_services.Engine.IsAvailable || audio is null || !File.Exists(audio) || !_backfilling.Add(take.Id)) return;
        try
        {
            var floor = take.Register?.FloorHz ?? Core.Analysis.AnalysisPostProcessor.DefaultRegisterFloorHz;
            var result = await _services.Engine.AnalyzeAsync(audio, floor);
            await Task.Run(() => _services.Store.SaveDetail(take, result.Detail));
            if (Active?.Recording.Id == take.Id) UpdateActive();
        }
        catch (Exception)
        {
            // Non-essential: the pitch-based per-phrase trends are already shown.
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
            v => v.Audio is { } a ? Existing(Path.Combine(_services.ReferenceDir, a)) : null,
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
            return File.Exists(path) ? EuphoniaJson.ReadReferences(File.ReadAllText(path, Encoding.UTF8)) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
