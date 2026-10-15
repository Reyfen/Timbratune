using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared.Platform;
using static Reyfen.Timbratune.Core.Domain.Zones;

namespace Reyfen.Timbratune.ViewModels;

/// <summary>One card under "All recordings" (RecordingCard.tsx + its WaveformPlayer).</summary>
public sealed partial class RecordingItemViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly IFileDialogs _dialogs;
    private readonly IFileSharer? _sharer;
    private readonly Func<int, Task> _delete;
    private readonly Func<Recording, string, bool, Task<string?>> _exportData;
    private readonly Func<Recording, string, Task> _rename;

    /// <param name="sharer">The share sheet where the platform has one (Android): the take is then shared instead of saved.</param>
    /// <param name="exportData">Writes the take's .tmbr file (take, its audio path, share instead of save); returns where it was saved, or null if cancelled or shared.</param>
    public RecordingItemViewModel(Recording r, string? audioPath, bool isLatest, PlaybackService playback,
        IFileDialogs dialogs, IFileSharer? sharer, Func<int, Task> delete, Func<Recording, string, bool, Task<string?>> exportData,
        Func<Recording, string, Task> rename)
    {
        Recording = r;
        AudioPath = audioPath is not null && File.Exists(audioPath) ? audioPath : null;
        IsLatest = isLatest;
        _playback = playback;
        _dialogs = dialogs;
        _sharer = sharer;
        _delete = delete;
        _exportData = exportData;
        _rename = rename;
        _playback.PropertyChanged += OnPlaybackChanged;
        if (AudioPath is not null) _ = LoadPeaksAsync(AudioPath);
    }

    public Recording Recording { get; }
    public int Id => Recording.Id;
    public string Label => Recording.Label;
    public string PickerText => $"#{Recording.Id} · {Recording.Label}{(IsLatest ? " 🌟" : "")}";
    public string DateLine => $"{Recording.Date} · {Fmt(Recording.DurationS, "s")}";
    public bool IsLatest { get; }
    public string? Note => string.IsNullOrWhiteSpace(Recording.Note) ? null : "📝 " + Recording.Note;
    public bool HasNote => Note is not null;

    public string PitchChip => Fmt(Recording.Pitch.MeanHz);
    public string RangeChip => $"{Fmt(Recording.Pitch.MinHz)}–{Fmt(Recording.Pitch.MaxHz)}";
    public string F2Chip => Fmt(Recording.Formants.F2Hz);
    public string LoudChip => Fmt(Recording.Intensity.MeanDb);
    public string VariabilityChip => Fmt(Recording.Pitch.SdHz);
    public string HnrChip => Fmt(Recording.VoiceQuality.HnrDb);

    public string? AudioPath { get; }
    public bool HasAudio => AudioPath is not null;

    [ObservableProperty] private float[]? _peaks;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDialogOpen))] private bool _isConfirmingDelete;

    /// <summary>The save / export (or, on a phone, share) choices are shown.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDialogOpen))] private bool _isSaveMenuOpen;

    /// <summary>
    /// The platform has a share sheet (Android): the card shows a share button instead of 💾, and
    /// its choices hand the audio / data to another app instead of saving them to a folder.
    /// </summary>
    public bool CanShare => _sharer is not null;
    public bool CanSave => !CanShare;
    public string MenuTitle => CanShare ? "share" : "save or export";
    public string AudioChoice => CanShare ? "🎵 share audio (.wav)" : "🎵 save audio (.wav)";
    public string DataChoice => CanShare ? "📦 share data (.tmbr)" : "📦 export data (.tmbr)";
    public string PdfChoice => CanShare ? "📄 share PDF · coming soon" : "📄 export PDF · coming soon";

    /// <summary>This take's dialog (save / export, rename or delete) is open over the page.</summary>
    public bool IsDialogOpen => IsSaveMenuOpen || IsRenaming || IsConfirmingDelete;
    public string DialogTitle => $"#{Id} · {Label}";

    /// <summary>A .tmbr export is being prepared or written (an older take is analyzed first).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SaveTip))] private bool _isExporting;

    /// <summary>The last export's outcome ("exported to …" / "couldn't export …"); null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasExportStatus))] private string? _exportStatus;

    public bool HasExportStatus => ExportStatus is not null;

    /// <summary>The label is being edited.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDialogOpen))] private bool _isRenaming;
    [ObservableProperty] private string _renameText = "";
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

    public string ConfirmText => $"delete take #{Id}? this can't be undone" +
                                 (HasAudio ? " — you can save a copy of the audio first if you want to keep it." : ".");

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

    [RelayCommand]
    private void OpenSaveMenu()
    {
        IsRenaming = false;
        IsConfirmingDelete = false;
        IsSaveMenuOpen = true;
    }

    /// <summary>Closes whichever dialog is open, as cancel.</summary>
    [RelayCommand]
    private void CloseDialog()
    {
        IsSaveMenuOpen = false;
        IsRenaming = false;
        IsConfirmingDelete = false;
    }

    /// <summary>The audio: a copy saved through the file picker, or (<see cref="CanShare"/>) shared.</summary>
    [RelayCommand]
    private async Task SaveCopyAsync()
    {
        IsSaveMenuOpen = false;
        if (AudioPath is not { } path) return;
        var name = $"voice-take-{Id}{Path.GetExtension(path)}";
        try
        {
            if (_sharer is null)
            {
                await _dialogs.SaveCopyAsync(path, name);
                return;
            }
            var mime = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : "application/octet-stream";
            await _sharer.ShareAsync("Share this take's audio", name, mime, async target =>
            {
                await using var source = File.OpenRead(path);
                await source.CopyToAsync(target);
            });
        }
        catch (Exception ex)
        {
            ExportStatus = $"couldn't {(CanShare ? "share" : "save")} the audio 🌧️ {ex.Message}";
        }
    }

    /// <summary>The .tmbr file: saved through the file picker, or (<see cref="CanShare"/>) shared.</summary>
    [RelayCommand]
    private async Task ExportDataAsync()
    {
        IsSaveMenuOpen = false;
        if (AudioPath is null || IsExporting) return;
        IsExporting = true;
        ExportStatus = CanShare ? "preparing the file to share…" : "preparing the export…";
        try
        {
            var where = await _exportData(Recording, AudioPath, CanShare);
            ExportStatus = where is null ? null : $"exported to {Path.GetFileName(where)} ✨";
        }
        catch (Exception ex)
        {
            ExportStatus = $"couldn't {(CanShare ? "share" : "export")} this take 🌧️ {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void StartRename()
    {
        RenameText = Recording.Label;
        IsConfirmingDelete = false;
        IsSaveMenuOpen = false;
        IsRenaming = true;
    }

    [RelayCommand] private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        var label = RenameText.Trim();
        IsRenaming = false;
        if (label.Length == 0 || label == Recording.Label) return;
        try
        {
            await _rename(Recording, label);
        }
        catch (Exception ex)
        {
            ExportStatus = $"couldn't rename this take 🌧️ {ex.Message}";
        }
    }

    [RelayCommand]
    private void AskDelete()
    {
        IsRenaming = false;
        IsSaveMenuOpen = false;
        IsConfirmingDelete = true;
    }

    [RelayCommand] private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (IsCurrent) _playback.Stop(); // release the file before deleting it
        await _delete(Id);
    }

    private async Task LoadPeaksAsync(string path)
    {
        Peaks = await Task.Run(() => WaveformPeaks.FromWav(path, 400));
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
