using System.Buffers.Binary;
using System.Text;

namespace Euphonia.Core.Audio;

/// <summary>Minimal PCM WAV writer/reader — enough for our own recordings.</summary>
public sealed class WavWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _sampleRate;
    private readonly short _channels;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(string path, int sampleRate, int channels = 1)
    {
        _sampleRate = sampleRate;
        _channels = (short)channels;
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeader(); // placeholder sizes, patched on dispose
    }

    public string Path => _stream.Name;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)_dataBytes / (2 * _channels * _sampleRate));

    /// <summary>The PCM16 value a float sample (−1..1) is stored as.</summary>
    public static short ToPcm16(float sample) => (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);

    /// <summary>Appends float samples (−1..1, interleaved) as PCM16.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        Span<byte> buffer = stackalloc byte[4096];
        var i = 0;
        while (i < samples.Length)
        {
            var n = Math.Min(samples.Length - i, buffer.Length / 2);
            for (var k = 0; k < n; k++)
                BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(k * 2), ToPcm16(samples[i + k]));
            _stream.Write(buffer[..(n * 2)]);
            _dataBytes += n * 2;
            i += n;
        }
    }

    /// <summary>
    /// Shortens a finished mono PCM16 file written by this class (44-byte header) to its
    /// first <paramref name="sampleCount"/> samples, fixing the header sizes.
    /// </summary>
    public static void Truncate(string path, int sampleCount)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var dataBytes = Math.Min((long)sampleCount * 2, Math.Max(0, stream.Length - 44));
        stream.SetLength(44 + dataBytes);
        Span<byte> size = stackalloc byte[4];
        stream.Position = 4;
        BinaryPrimitives.WriteInt32LittleEndian(size, (int)(36 + dataBytes));
        stream.Write(size);
        stream.Position = 40;
        BinaryPrimitives.WriteInt32LittleEndian(size, (int)dataBytes);
        stream.Write(size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Position = 0;
        WriteHeader();
        _stream.Dispose();
    }

    private void WriteHeader()
    {
        Span<byte> h = stackalloc byte[44];
        var blockAlign = (short)(_channels * 2);
        Encoding.ASCII.GetBytes("RIFF", h[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], (int)(36 + _dataBytes));
        Encoding.ASCII.GetBytes("WAVE", h[8..12]);
        Encoding.ASCII.GetBytes("fmt ", h[12..16]);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(h[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(h[22..], _channels);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], _sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], _sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h[34..], 16);
        Encoding.ASCII.GetBytes("data", h[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(h[40..], (int)_dataBytes);
        _stream.Write(h);
    }
}

public static class WaveformPeaks
{
    /// <summary>
    /// Reads a PCM16/PCM8/float WAV and returns <paramref name="bins"/> peak
    /// amplitudes normalised to 0..1 (like wavesurfer's normalize: true).
    /// Returns an empty array for anything that isn't a readable WAV.
    /// </summary>
    public static float[] FromWav(string path, int bins)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return [];
            reader.ReadInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return [];

            short format = 1, channels = 1, bits = 16;
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
            {
                var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var size = reader.ReadInt32();
                if (id == "fmt ")
                {
                    format = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bits = reader.ReadInt16();
                    reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
                }
                else if (id == "data")
                {
                    var bytesPerSample = bits / 8;
                    if (bytesPerSample == 0 || channels == 0) return [];
                    var available = Math.Min(size, (int)(reader.BaseStream.Length - reader.BaseStream.Position));
                    var data = reader.ReadBytes(available);
                    var frames = data.Length / (bytesPerSample * channels);
                    return Bin(data, frames, channels, bytesPerSample, format == 3, bins);
                }
                else
                {
                    reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
                }
            }
        }
        catch (IOException) { } // includes EndOfStreamException for truncated files
        return [];
    }

    private static float[] Bin(byte[] data, int frames, int channels, int bytesPerSample, bool isFloat, int bins)
    {
        if (frames == 0 || bins <= 0) return [];
        var peaks = new float[bins];
        var frameBytes = bytesPerSample * channels;
        for (var f = 0; f < frames; f++)
        {
            var o = f * frameBytes; // first channel is enough for a preview
            float v = bytesPerSample switch
            {
                2 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(o)) / 32768f,
                4 when isFloat => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(o)),
                4 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o)) / 2147483648f,
                1 => (data[o] - 128) / 128f,
                _ => 0f,
            };
            var b = (int)((long)f * bins / frames);
            peaks[b] = Math.Max(peaks[b], Math.Abs(v));
        }
        var max = peaks.Max();
        if (max > 0)
            for (var i = 0; i < bins; i++) peaks[i] /= max;
        return peaks;
    }
}
