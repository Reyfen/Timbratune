using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Euphonia.Core.Audio;

namespace Euphonia.Services;

/// <summary>
/// The single app-wide player: starting a clip stops whatever was playing
/// (the shared-singleton rule from WaveformPlayer.tsx). View models watch
/// <see cref="CurrentPath"/> to know whether "their" clip is the live one.
/// </summary>
public sealed partial class PlaybackService : ObservableObject, IDisposable
{
    private readonly IAudioPlayer _player;
    private readonly DispatcherTimer _timer;

    public PlaybackService(IAudioPlayer player)
    {
        _player = player;
        _player.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(OnEnded);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Poll());
    }

    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private string? _lastError;

    public double Progress => Duration.TotalSeconds > 0 ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;

    /// <summary>Play/pause <paramref name="path"/>; switching clips starts the new one from the top.</summary>
    public void Toggle(string path)
    {
        if (CurrentPath == path)
        {
            if (IsPlaying) Pause();
            else Resume();
            return;
        }
        Play(path);
    }

    public void Play(string path)
    {
        try
        {
            LastError = null;
            _player.Play(path);
            CurrentPath = path;
            IsPlaying = true;
            Poll();
            _timer.Start();
        }
        catch (Exception ex)
        {
            LastError = $"couldn't play that clip 🌧️ — {ex.Message}";
            Stop();
        }
    }

    public void Pause()
    {
        _player.Pause();
        IsPlaying = false;
        _timer.Stop();
        Poll();
    }

    public void Resume()
    {
        if (CurrentPath is null) return;
        _player.Resume();
        IsPlaying = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _player.Stop();
        CurrentPath = null;
        IsPlaying = false;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        OnPropertyChanged(nameof(Progress));
    }

    public void Seek(string path, double fraction)
    {
        if (CurrentPath != path) Play(path);
        if (CurrentPath != path || _player.Duration <= TimeSpan.Zero) return;
        _player.Seek(_player.Duration * Math.Clamp(fraction, 0, 1));
        Poll();
    }

    private void Poll()
    {
        Position = _player.Position;
        Duration = _player.Duration;
        OnPropertyChanged(nameof(Progress));
    }

    private void OnEnded() => Stop();

    public void Dispose()
    {
        _timer.Stop();
        _player.Dispose();
    }
}
