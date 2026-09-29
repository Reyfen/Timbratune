namespace Euphonia.Core.Audio;

/// <summary>
/// Developer stand-in for the microphone: plays a WAV file into the recorder in
/// real time (10 ms blocks), writing the take and raising
/// <see cref="SamplesCaptured"/> exactly as live capture does. Lets the live
/// analysis be tried and demonstrated without speaking (EUPHONIA_FAKE_MIC).
/// </summary>
public sealed class FileReplayRecorder(string wavPath) : IAudioRecorder
{
    public const string EnvVar = "EUPHONIA_FAKE_MIC";

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private WavWriter? _writer;

    public bool IsRecording => _pump is not null;
    public int SampleRate { get; private set; } = 44100;
    public event SamplesCapturedHandler? SamplesCaptured;

    public void Start()
    {
        Acoustics.Sound sound;
        using (var stream = File.OpenRead(wavPath)) sound = Acoustics.WavDecoder.Decode(stream);
        SampleRate = (int)Math.Round(sound.SamplingFrequency);
        var samples = sound.ToMono().Select(v => (float)v).ToArray();
        var path = Path.Combine(Path.GetTempPath(), $"euphonia-take-{Guid.NewGuid():N}.wav");
        _writer = new WavWriter(path, SampleRate);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var block = SampleRate / 100;
        _pump = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            for (var i = 0; i < samples.Length && !token.IsCancellationRequested; i += block)
            {
                var n = Math.Min(block, samples.Length - i);
                lock (_gate)
                {
                    _writer?.Write(samples.AsSpan(i, n));
                    SamplesCaptured?.Invoke(samples.AsSpan(i, n));
                }
                // Stay in real time: wait until this block's end time has passed.
                var due = started + TimeSpan.FromSeconds((double)(i + n) / SampleRate);
                var wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, CancellationToken.None);
            }
        });
    }

    public async Task<string> StopAsync()
    {
        _cts?.Cancel();
        if (_pump is not null) await _pump;
        lock (_gate)
        {
            var path = _writer!.Path;
            _writer.Dispose();
            _writer = null;
            _pump = null;
            return path;
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        try { _pump?.Wait(); } catch (AggregateException) { }
        lock (_gate)
        {
            if (_writer is null) return;
            var path = _writer.Path;
            _writer.Dispose();
            _writer = null;
            _pump = null;
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    public void Dispose() => Cancel();
}
