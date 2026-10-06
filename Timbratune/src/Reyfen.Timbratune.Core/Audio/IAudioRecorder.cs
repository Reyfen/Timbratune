namespace Reyfen.Timbratune.Core.Audio;

/// <summary>Receives captured samples; the span is only valid during the call.</summary>
public delegate void SamplesCapturedHandler(ReadOnlySpan<float> samples);

/// <summary>Microphone capture straight to a mono PCM16 WAV file (what the analysis engine reads).</summary>
public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }

    /// <summary>Sample rate of the recording (Hz).</summary>
    int SampleRate { get; }

    /// <summary>
    /// Raised on the audio thread for every captured block, right after it was written to
    /// the WAV — in the same order, so a listener sees exactly the samples being saved.
    /// </summary>
    event SamplesCapturedHandler? SamplesCaptured;

    /// <summary>Starts capturing from the default input device.</summary>
    void Start();

    /// <summary>Stops capturing and returns the path of the finished WAV file (in a temp folder).</summary>
    Task<string> StopAsync();

    /// <summary>Stops and throws the recording away.</summary>
    void Cancel();
}

/// <summary>One playable clip. Only one clip plays at a time app-wide (see <see cref="IAudioPlayer"/>).</summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>The path currently loaded, or null.</summary>
    string? CurrentPath { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    float Volume { get; set; }

    /// <summary>Loads <paramref name="path"/> (stopping whatever was playing) and plays it.</summary>
    void Play(string path);
    void Pause();
    void Resume();
    void Stop();
    void Seek(TimeSpan position);

    /// <summary>Raised on the audio thread when a clip finishes on its own.</summary>
    event EventHandler? PlaybackEnded;
}
