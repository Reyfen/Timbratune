using AVFoundation;
using Foundation;
using Reyfen.Timbratune.Core.Audio;

namespace Reyfen.Timbratune.iOS;

/// <summary>
/// The audio session: record and play back at once (playback through the loudspeaker, not the
/// earpiece). Bluetooth headsets aren't offered for input: their microphones run at 8-16 kHz
/// (HFP), too little for the formants.
/// <para>
/// While recording, the session is in measurement mode, so iOS applies no automatic gain, noise
/// suppression or echo cancellation to the voice being analysed; the input gain is raised to its
/// maximum then, as measurement mode records the built-in microphone very quietly (takes peaked
/// at -38 to -32 dBFS on iPhones in 0.3.2). The rest of the time the session is in the default
/// mode, as measurement mode also plays back quietly.
/// </para>
/// </summary>
internal static class IosAudioSession
{
    public static void Configure()
    {
        var session = AVAudioSession.SharedInstance();
        if (session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetoothA2DP) is { } error)
            Console.WriteLine($"Timbratune: audio session category: {error.LocalizedDescription}");
        SetMode(AVAudioSession.ModeDefault);
        session.SetPreferredSampleRate(44100, out _);
        session.SetActive(true, out _);
    }

    /// <summary>Before the microphone opens: measurement mode, input gain at its maximum. Returns what was set, for the report.</summary>
    public static string EnterRecording()
    {
        var session = AVAudioSession.SharedInstance();
        var mode = SetMode(AVAudioSession.ModeMeasurement);
        var gainBefore = session.InputGain;
        string gain;
        if (!session.InputGainSettable)
            gain = $"{gainBefore:0.00}, not settable";
        else if (session.SetInputGain(1f, out var gainError))
            gain = $"{gainBefore:0.00} -> {session.InputGain:0.00}";
        else
            gain = $"{gainBefore:0.00}, raising failed: {gainError?.LocalizedDescription}";
        var input = session.CurrentRoute.Inputs is { Length: > 0 } inputs ? $"{inputs[0].PortName} ({inputs[0].PortType})" : "none";
        return $"mode {mode} · input gain {gain} · input {input} · {session.SampleRate:0} Hz";
    }

    /// <summary>After the microphone closed: back to the default mode, for playback.</summary>
    public static void LeaveRecording() => SetMode(AVAudioSession.ModeDefault);

    private static string SetMode(NSString mode)
    {
        var session = AVAudioSession.SharedInstance();
        return session.SetMode(mode, out var error)
            ? $"{session.Mode}"
            : $"{session.Mode} (setting {mode} failed: {error?.LocalizedDescription})";
    }
}

/// <summary>
/// The recorder with the audio session around it (<see cref="IosAudioSession"/>), and a line per
/// take in <see cref="AudioReport"/>: the session it recorded with, its length and peak level.
/// </summary>
internal sealed class IosRecorder : IAudioRecorder
{
    private readonly IAudioRecorder _inner;
    private float _peak;
    private long _samples;

    public IosRecorder(IAudioRecorder inner)
    {
        _inner = inner;
        _inner.SamplesCaptured += OnSamples;
    }

    public bool IsRecording => _inner.IsRecording;

    public int SampleRate => _inner.SampleRate;

    public event SamplesCapturedHandler? SamplesCaptured;

    public void Start()
    {
        if (_inner.IsRecording) return;
        _peak = 0;
        _samples = 0;
        AudioReport.Write("record · " + IosAudioSession.EnterRecording());
        try
        {
            _inner.Start();
        }
        catch
        {
            IosAudioSession.LeaveRecording();
            throw;
        }
    }

    public async Task<string> StopAsync()
    {
        try
        {
            return await _inner.StopAsync();
        }
        finally
        {
            IosAudioSession.LeaveRecording();
            var peakDb = _peak > 0 ? $"{20 * Math.Log10(_peak):0.0} dBFS" : "silent";
            AudioReport.Write($"stop · {(double)_samples / SampleRate:0.0} s · peak {peakDb}");
        }
    }

    public void Cancel()
    {
        var was = _inner.IsRecording;
        _inner.Cancel();
        if (was) IosAudioSession.LeaveRecording();
    }

    public void Dispose()
    {
        _inner.SamplesCaptured -= OnSamples;
        _inner.Dispose();
    }

    // Audio thread.
    private void OnSamples(ReadOnlySpan<float> samples)
    {
        var peak = _peak;
        foreach (var s in samples)
            if (Math.Abs(s) > peak) peak = Math.Abs(s);
        _peak = peak;
        _samples += samples.Length;
        SamplesCaptured?.Invoke(samples);
    }
}

/// <summary>
/// Documents/audio.txt (Files app › Timbratune): the audio session of each take, newest first and
/// kept short, as a Release build's own log lines are redacted in the device log (see <see cref="CrashLog"/>).
/// </summary>
internal static class AudioReport
{
    private const int MaxBytes = 16 * 1024;

    public static void Write(string line)
    {
        try
        {
            var path = Path.Combine(IosFolders.Documents, "audio.txt");
            var old = File.Exists(path) ? File.ReadAllText(path) : "";
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {line}\n" + old;
            if (text.Length > MaxBytes) text = text[..MaxBytes];
            File.WriteAllText(path, text);
        }
        catch (Exception)
        {
            // A report that can't be written must not stop a recording.
        }
    }
}
