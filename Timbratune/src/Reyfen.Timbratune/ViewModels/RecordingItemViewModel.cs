using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Services;
using static Reyfen.Timbratune.Core.Domain.Zones;

namespace Reyfen.Timbratune.ViewModels;

/// <summary>One card under "All recordings" (RecordingCard.tsx + its WaveformPlayer).</summary>
public sealed partial class RecordingItemViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly IFileDialogs _dialogs;
    private readonly Func<int, Task> _delete;

    public RecordingItemViewModel(Recording r, string? audioPath, bool isLatest, PlaybackService playback,
        IFileDialogs dialogs, Func<int, Task> delete)
    {
        Recording = r;
        AudioPath = audioPath is not null && File.Exists(audioPath) ? audioPath : null;
        IsLatest = isLatest;
        _playback = playback;
        _dialogs = dialogs;
        _delete = delete;
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
    [ObservableProperty] private bool _isConfirmingDelete;

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
    private async Task SaveCopyAsync()
    {
        if (AudioPath is not null) await _dialogs.SaveCopyAsync(AudioPath, $"voice-take-{Id}{Path.GetExtension(AudioPath)}");
    }

    [RelayCommand] private void AskDelete() => IsConfirmingDelete = true;
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
