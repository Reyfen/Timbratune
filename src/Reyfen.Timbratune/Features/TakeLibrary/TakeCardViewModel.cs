using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Navigation;
using static Reyfen.Timbratune.Core.Domain.Zones;

namespace Reyfen.Timbratune.Features.TakeLibrary;

/// <summary>A chip on a take's card: the value, and what it is ("pitch Hz").</summary>
public sealed record TakeChip(string Value, string Label);

/// <summary>One card under "All recordings" (RecordingCard.tsx + its WaveformPlayer).</summary>
public sealed partial class TakeCardViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly ITakeActionsLauncher _actions;
    private readonly Action<TakeCardViewModel> _select;

    /// <param name="audioPath">The take's audio file, when it is there.</param>
    /// <param name="canShare">The platform has a share sheet (Android): the card shows a share button instead of 💾.</param>
    /// <param name="loadPeaks">Reads the waveform's peaks from the audio file (off the UI thread).</param>
    /// <param name="chips">The metrics with a <see cref="MetricInfo.Chip"/>, in registry order.</param>
    public TakeCardViewModel(Recording r, string? audioPath, bool isLatest, PlaybackService playback,
        ITakeActionsLauncher actions, bool canShare, Action<TakeCardViewModel> select, Func<string, Task<float[]>> loadPeaks,
        IReadOnlyList<MetricInfo> chips)
    {
        Chips = chips.Select(m => new TakeChip(m.Chip!.Text?.Invoke(r) ?? Fmt(m.Take?.Invoke(r)), m.Chip.Label)).ToList();
        Recording = r;
        AudioPath = audioPath;
        IsLatest = isLatest;
        CanShare = canShare;
        _playback = playback;
        _actions = actions;
        _select = select;
        _playback.PropertyChanged += OnPlaybackChanged;
        if (AudioPath is not null) _ = LoadPeaksAsync(AudioPath, loadPeaks);
    }

    public Recording Recording { get; }
    public int Id => Recording.Id;
    public string Label => Recording.Label;
    public string PickerText => $"#{Recording.Id} · {Recording.Label}{(IsLatest ? " 🌟" : "")}";
    public string DateLine => $"{Recording.Date} · {Fmt(Recording.DurationS, "s")}";
    public bool IsLatest { get; }
    public string? Note => string.IsNullOrWhiteSpace(Recording.Note) ? null : "📝 " + Recording.Note;
    public bool HasNote => Note is not null;

    public IReadOnlyList<TakeChip> Chips { get; }

    public string? AudioPath { get; }
    public bool HasAudio => AudioPath is not null;

    [ObservableProperty] private float[]? _peaks;
    [ObservableProperty] private bool _isActive;

    /// <summary>
    /// The platform has a share sheet (Android): the card shows a share button instead of 💾, and
    /// its choices hand the audio / data to another app instead of saving them to a folder.
    /// </summary>
    public bool CanShare { get; }
    public bool CanSave => !CanShare;

    /// <summary>A .tmbr export is being prepared or written (an older take is analyzed first).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SaveTip))] private bool _isExporting;

    /// <summary>The last action's outcome ("exported to …" / "couldn't export …"); null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasExportStatus))] private string? _exportStatus;

    public bool HasExportStatus => ExportStatus is not null;
    public string SaveTip => IsExporting ? "exporting…" : CanShare ? "share this take" : "save or export this take";

    public bool IsCurrent => AudioPath is not null && _playback.CurrentPath == AudioPath;
    public bool IsPlaying => IsCurrent && _playback.IsPlaying;
    // Plain glyphs, not emoji: ⏸ renders as a colour-emoji box on Windows.
    public string PlayGlyph => IsPlaying ? "❚❚" : "▶";
    public double Progress => IsCurrent ? _playback.Progress : 0;

    public string TimeText
    {
        get
        {
            var total = IsCurrent && _playback.Duration > TimeSpan.Zero
                ? _playback.Duration
                : TimeSpan.FromSeconds(Recording.DurationS ?? 0);
            var pos = IsCurrent ? _playback.Position : TimeSpan.Zero;
            return $"{pos:m\\:ss} / {total:m\\:ss}";
        }
    }

    /// <summary>What an action on this take reports (TakeActions, through a message).</summary>
    public void Apply(TakeActionStatus status)
    {
        IsExporting = status.Busy;
        ExportStatus = status.Text;
    }

    /// <summary>Shows this take above (the switcher's chip, or a click on the card).</summary>
    [RelayCommand]
    private void Select() => _select(this);

    [RelayCommand]
    private void PlayPause()
    {
        if (AudioPath is not null) _playback.Toggle(AudioPath);
    }

    [RelayCommand]
    private void Seek(double fraction)
    {
        if (AudioPath is not null) _playback.Seek(AudioPath, fraction);
    }

    [RelayCommand] private void OpenSaveMenu() => _actions.OpenSaveMenu(Id);

    [RelayCommand] private void StartRename() => _actions.OpenRename(Id);

    [RelayCommand] private void AskDelete() => _actions.OpenDelete(Id);

    private async Task LoadPeaksAsync(string path, Func<string, Task<float[]>> loadPeaks)
    {
        Peaks = await loadPeaks(path);
    }

    private void OnPlaybackChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(TimeText));
    }

    public void Dispose() => _playback.PropertyChanged -= OnPlaybackChanged;
}
