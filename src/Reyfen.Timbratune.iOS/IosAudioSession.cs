using AVFoundation;
using Reyfen.Timbratune.Core.Audio;

namespace Reyfen.Timbratune.iOS;

/// <summary>
/// The audio session: record and play back at once (playback through the loudspeaker, not the
/// earpiece). Bluetooth headsets aren't offered for input: their microphones run at 8-16 kHz
/// (HFP), too little for the formants.
/// <para>
/// While recording, the session is in measurement mode, so iOS applies no automatic gain, noise
/// suppression or echo cancellation to the voice being analysed. Measurement mode records quietly
/// at iOS's default input gain (0.50 on an iPad Air: peaks at -38 dBFS in 0.3.2), so the gain is
/// set to <see cref="RecordingGain"/> where iOS allows it (1.0 was too much there: +32 dB, a loud
/// voice clipped). An iPhone 12 doesn't allow it (fixed at 1.0, peaks near -26 dBFS). The rest of
/// the time the session is in the default mode, as measurement mode also plays back quietly.
/// </para>
/// </summary>
internal static class IosAudioSession
{
    /// <summary>The input gain while recording, where it can be set (0 to 1; iPad Air: 0.5 to 1.0 is +32 dB).</summary>
    public const float RecordingGain = 0.8f;

    public static void Configure()
    {
        var session = AVAudioSession.SharedInstance();
        if (session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetoothA2DP) is { } error)
            Console.WriteLine($"Timbratune: audio session category: {error.LocalizedDescription}");
        SetMode(AVAudioSessionMode.Default);
        session.SetPreferredSampleRate(44100, out _);
        session.SetActive(true, out _);
    }

    /// <summary>Before the microphone opens: measurement mode, then <see cref="ApplyGain"/>. Returns what was set, for the report.</summary>
    public static string EnterRecording(out bool gainSettable) =>
        $"mode {SetMode(AVAudioSessionMode.Measurement)} · input gain {ApplyGain(out gainSettable)}";

    /// <summary>
    /// Sets the input gain to <see cref="RecordingGain"/>; <paramref name="settable"/> is false when
    /// iOS doesn't allow it now (also while the input route is still changing after the mode, so the
    /// recorder tries once more with the microphone open). Returns what happened, for the report.
    /// </summary>
    public static string ApplyGain(out bool settable)
    {
        var session = AVAudioSession.SharedInstance();
        var before = session.InputGain;
        settable = session.InputGainSettable;
        var gain = !settable ? $"{before:0.00}, not settable"
            : session.SetInputGain(RecordingGain, out var error) ? $"{before:0.00} -> {session.InputGain:0.00}"
            : $"{before:0.00}, setting failed: {error?.LocalizedDescription}";
        var input = session.CurrentRoute.Inputs is { Length: > 0 } inputs ? $"{inputs[0].PortName} ({inputs[0].PortType})" : "none";
        return $"{gain} · input {input} · {session.SampleRate:0} Hz";
    }

    /// <summary>After the microphone closed: back to the default mode, for playback.</summary>
    public static void LeaveRecording() => SetMode(AVAudioSessionMode.Default);

    private static string SetMode(AVAudioSessionMode mode)
    {
        var session = AVAudioSession.SharedInstance();
        return session.SetMode(mode, out var error)
            ? $"{session.Mode}"
            : $"{session.Mode} (setting {mode} failed: {error?.LocalizedDescription})";
    }
}

/// <summary>
/// The recorder with the audio session around it (<see cref="IosAudioSession"/>), and a line per
/// take in <see cref="AudioReport"/>: the session it recorded with, its length, peak level and
/// clipped samples (the recorder limits samples to full scale, see SoundFlowRecorder).
/// </summary>
internal sealed class IosRecorder : IAudioRecorder
{
    private readonly IAudioRecorder _inner;
    private float _peak;
    private long _samples;
    private long _clipped;

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
        _clipped = 0;
        AudioReport.Write("record · " + IosAudioSession.EnterRecording(out var gainSettable));
        try
        {
            _inner.Start();
            // Not settable before the microphone opened (the route may still have been changing): once more.
            if (!gainSettable)
                AudioReport.Write("microphone open · input gain " + IosAudioSession.ApplyGain(out _));
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
            AudioReport.Write($"stop · {(double)_samples / SampleRate:0.0} s · peak {peakDb} · clipped {_clipped} samples");
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
        {
            var a = Math.Abs(s);
            if (a > peak) peak = a;
            if (a >= 1f) _clipped++;
        }
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
