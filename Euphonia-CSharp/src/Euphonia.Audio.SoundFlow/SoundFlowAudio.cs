using Euphonia.Core.Audio;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Euphonia.Audio.SoundFlow;

/// <summary>
/// Owns the one miniaudio engine for the app. Create once, share between the
/// recorder and the player, dispose on exit.
/// </summary>
public sealed class SoundFlowAudio : IDisposable
{
    /// <summary>44.1 kHz mono — what analyze.py converted everything to before analysis.</summary>
    public const int SampleRate = 44100;

    public SoundFlowAudio()
    {
        Engine = new MiniAudioEngine();
    }

    internal MiniAudioEngine Engine { get; }

    public IAudioRecorder CreateRecorder() => new SoundFlowRecorder(Engine);
    public IAudioPlayer CreatePlayer() => new SoundFlowPlayer(Engine);

    public void Dispose() => Engine.Dispose();

    internal static AudioFormat Format(int channels) => new()
    {
        Format = SampleFormat.F32,
        SampleRate = SampleRate,
        Channels = channels,
        Layout = AudioFormat.GetLayoutFromChannels(channels),
    };
}

/// <summary>Captures the default microphone as mono float and streams it into a PCM16 WAV.</summary>
internal sealed class SoundFlowRecorder(MiniAudioEngine engine) : IAudioRecorder
{
    private readonly object _gate = new();
    private AudioCaptureDevice? _device;
    private WavWriter? _writer;

    public bool IsRecording => _device is not null;

    public void Start()
    {
        lock (_gate)
        {
            if (_device is not null) return;
            var path = Path.Combine(Path.GetTempPath(), $"euphonia-take-{Guid.NewGuid():N}.wav");
            _writer = new WavWriter(path, SoundFlowAudio.SampleRate, channels: 1);
            try
            {
                _device = engine.InitializeCaptureDevice(null, SoundFlowAudio.Format(1));
                _device.OnAudioProcessed += OnSamples;
                _device.Start();
            }
            catch
            {
                CleanupUnlocked(deleteFile: true);
                throw;
            }
        }
    }

    public Task<string> StopAsync()
    {
        lock (_gate)
        {
            if (_writer is null) throw new InvalidOperationException("Not recording.");
            var path = _writer.Path;
            CleanupUnlocked(deleteFile: false);
            return Task.FromResult(path);
        }
    }

    public void Cancel()
    {
        lock (_gate) CleanupUnlocked(deleteFile: true);
    }

    public void Dispose() => Cancel();

    private void OnSamples(Span<float> samples, Capability capability)
    {
        lock (_gate) _writer?.Write(samples);
    }

    private void CleanupUnlocked(bool deleteFile)
    {
        if (_device is not null)
        {
            _device.OnAudioProcessed -= OnSamples;
            try { _device.Stop(); } catch (Exception) { /* device may already be gone */ }
            _device.Dispose();
            _device = null;
        }
        if (_writer is not null)
        {
            var path = _writer.Path;
            _writer.Dispose();
            _writer = null;
            if (deleteFile) try { File.Delete(path); } catch (IOException) { }
        }
    }
}

/// <summary>
/// One playback device + one SoundPlayer at a time. Playing a new clip stops
/// the previous one — the "only one player plays" rule from WaveformPlayer.tsx.
/// </summary>
internal sealed class SoundFlowPlayer(MiniAudioEngine engine) : IAudioPlayer
{
    private readonly object _gate = new();
    private AudioPlaybackDevice? _device;
    private SoundPlayer? _player;
    private Stream? _stream;
    private StreamDataProvider? _provider;
    private float _volume = 1f;

    public string? CurrentPath { get; private set; }
    public bool IsPlaying => _player?.State == PlaybackState.Playing;
    public TimeSpan Position => _player is { } p ? TimeSpan.FromSeconds(p.Time) : TimeSpan.Zero;
    public TimeSpan Duration => _player is { } p ? TimeSpan.FromSeconds(p.Duration) : TimeSpan.Zero;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_player is not null) _player.Volume = _volume;
        }
    }

    public event EventHandler? PlaybackEnded;

    public void Play(string path)
    {
        lock (_gate)
        {
            UnloadUnlocked();
            // Decode at the clip's own channel count; the device mixes to its output.
            _stream = File.OpenRead(path);
            var clipFormat = AudioFormat.GetFormatFromStream(_stream) ?? SoundFlowAudio.Format(1);
            _stream.Position = 0;
            var format = SoundFlowAudio.Format(clipFormat.Channels is > 0 and <= 2 ? clipFormat.Channels : 2);

            _device = engine.InitializePlaybackDevice(null, format);
            _provider = new StreamDataProvider(engine, _stream);
            _player = new SoundPlayer(engine, format, _provider) { Volume = _volume };
            _player.PlaybackEnded += OnEnded;
            _device.MasterMixer.AddComponent(_player);
            _device.Start();
            _player.Play();
            CurrentPath = path;
        }
    }

    public void Pause()
    {
        lock (_gate) _player?.Pause();
    }

    public void Resume()
    {
        lock (_gate) _player?.Play();
    }

    public void Stop()
    {
        lock (_gate) UnloadUnlocked();
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate) _player?.Seek(position, SeekOrigin.Begin);
    }

    public void Dispose() => Stop();

    private void OnEnded(object? sender, EventArgs e) => PlaybackEnded?.Invoke(this, EventArgs.Empty);

    private void UnloadUnlocked()
    {
        if (_player is not null)
        {
            _player.PlaybackEnded -= OnEnded;
            _player.Stop();
            _device?.MasterMixer.RemoveComponent(_player);
            _player.Dispose();
            _player = null;
        }
        _provider?.Dispose();
        _provider = null;
        if (_device is not null)
        {
            try { _device.Stop(); } catch (Exception) { }
            _device.Dispose();
            _device = null;
        }
        _stream?.Dispose();
        _stream = null;
        CurrentPath = null;
    }
}
