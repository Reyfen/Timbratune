using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;

namespace Reyfen.Timbratune.Core.Audio.Decoding;

/// <summary>
/// The one place decoded audio is converted for analysis: mixed down to mono and brought to
/// 44.1 kHz, written as a PCM16 WAV. Audio already at 44.1 kHz streams straight to the file
/// (mono untouched, other channel counts averaged). Other rates are collected as mono and
/// resampled in one pass when <see cref="Complete"/> is called, with the Acoustics
/// <see cref="Resampler"/> (the analysis holds the whole take in memory anyway).
/// </summary>
public sealed class AnalysisWavSink : IPcmSink, IDisposable
{
    /// <summary>The rate takes are stored at (what analyze.py converted everything to).</summary>
    public const int SampleRate = 44100;

    private const int ChunkFrames = 1 << 16;

    private readonly WavWriter _writer;
    private int _rate, _channels;
    private float[]? _mix;                           // a downmixed block
    private List<float[]>? _chunks;                  // mono at the source rate, waiting for Complete
    private int _lastChunkLength;
    private bool _completed;

    public AnalysisWavSink(Stream target, bool leaveOpen = false) => _writer = new WavWriter(target, SampleRate, 1, leaveOpen);

    /// <summary>Frames written to the WAV so far (all of them after <see cref="Complete"/>).</summary>
    public long Frames { get; private set; }

    /// <summary>Frames received from the decoder, at its own rate.</summary>
    public long SourceFrames { get; private set; }

    public void SetFormat(int sampleRate, int channels)
    {
        if (_rate != 0) throw new InvalidOperationException("The format was already set.");
        if (sampleRate <= 0 || channels <= 0) throw new InvalidDataException($"Not a usable audio format ({sampleRate} Hz, {channels} channels).");
        _rate = sampleRate;
        _channels = channels;
        if (sampleRate != SampleRate) _chunks = [];
    }

    public void Write(ReadOnlySpan<float> interleaved)
    {
        if (_rate == 0) throw new InvalidOperationException("SetFormat comes before the samples.");
        if (_completed) throw new InvalidOperationException("Already completed.");
        var frames = interleaved.Length / _channels;
        SourceFrames += frames;
        if (_chunks is null)
        {
            if (_channels == 1) _writer.Write(interleaved);
            else
                for (var f = 0; f < frames; f += ChunkFrames)
                {
                    var n = Math.Min(ChunkFrames, frames - f);
                    _writer.Write(Downmix(interleaved.Slice(f * _channels, n * _channels), n));
                }
            Frames += frames;
            return;
        }
        for (var f = 0; f < frames;)
        {
            if (_chunks.Count == 0 || _lastChunkLength == ChunkFrames)
            {
                _chunks.Add(new float[ChunkFrames]);
                _lastChunkLength = 0;
            }
            var n = Math.Min(ChunkFrames - _lastChunkLength, frames - f);
            var target = _chunks[^1].AsSpan(_lastChunkLength, n);
            if (_channels == 1) interleaved.Slice(f, n).CopyTo(target);
            else Downmix(interleaved.Slice(f * _channels, n * _channels), n).CopyTo(target);
            _lastChunkLength += n;
            f += n;
        }
    }

    /// <summary>Finishes the WAV (resampling what was collected); throws if no audio arrived.</summary>
    public void Complete()
    {
        if (_completed) return;
        _completed = true;
        if (SourceFrames == 0) throw new InvalidDataException("No audio could be decoded.");
        if (_chunks is null) return;

        var mono = new double[SourceFrames];
        var at = 0;
        foreach (var chunk in _chunks)
        {
            var n = (int)Math.Min(chunk.Length, SourceFrames - at);
            for (var i = 0; i < n; i++) mono[at + i] = chunk[i];
            at += n;
        }
        _chunks = null;
        var resampled = Resampler.Resample(new Sound(mono, _rate), SampleRate).Channel(0);
        var block = new float[ChunkFrames];
        for (var f = 0; f < resampled.Length; f += ChunkFrames)
        {
            var n = Math.Min(ChunkFrames, resampled.Length - f);
            for (var i = 0; i < n; i++) block[i] = (float)resampled[f + i];
            _writer.Write(block.AsSpan(0, n));
        }
        Frames = resampled.Length;
    }

    /// <summary>Closes the WAV (its header then says how much was written).</summary>
    public void Dispose() => _writer.Dispose();

    private ReadOnlySpan<float> Downmix(ReadOnlySpan<float> interleaved, int frames)
    {
        _mix ??= new float[ChunkFrames];
        var scale = 1f / _channels;
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            var frame = interleaved.Slice(f * _channels, _channels);
            foreach (var s in frame) sum += s;
            _mix[f] = sum * scale;
        }
        return _mix.AsSpan(0, frames);
    }
}
