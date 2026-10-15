using System.Buffers.Binary;
using System.Text;
using Reyfen.Timbratune.Acoustics;

namespace Reyfen.Timbratune.Core.Audio.Decoding;

/// <summary>
/// WAV (PCM 8–32 bit, float, <c>WAVE_FORMAT_EXTENSIBLE</c>, RF64) and AIFF/AIFC (big- and
/// little-endian PCM, float), in managed code: works everywhere, the browser included.
/// </summary>
public sealed class ManagedDecoderProvider : IAudioDecoderProvider
{
    public const string ProviderId = "managed";

    private static readonly FormatSet Formats = new([AudioFormatCatalog.Wav, AudioFormatCatalog.Aiff]);

    public string Id => ProviderId;

    public int Priority => 1;

    public ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default) => new(Formats);

    public IAudioDecoder Create() => new Decoder();

    private sealed class Decoder : IAudioDecoder
    {
        public Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.Run(() =>
            {
                using var opened = source.Open();
                using var stream = opened.CanSeek ? opened : Buffered(opened);
                Span<byte> magic = stackalloc byte[4];
                stream.ReadExactly(magic);
                stream.Position = 0;
                if (magic.SequenceEqual("FORM"u8)) AiffDecoder.Decode(stream, sink, progress, cancellationToken);
                else Write(WavDecoder.Decode(stream), sink, cancellationToken);
                progress?.Report(1);
            }, cancellationToken);

        public void Dispose()
        {
        }

        private static MemoryStream Buffered(Stream stream)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }

        private static void Write(Sound sound, IPcmSink sink, CancellationToken cancellationToken)
        {
            var channels = sound.ChannelCount;
            sink.SetFormat((int)Math.Round(sound.SamplingFrequency), channels);
            const int block = 8192;
            var buffer = new float[block * channels];
            for (var f = 0; f < sound.SampleCount; f += block)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = Math.Min(block, sound.SampleCount - f);
                for (var c = 0; c < channels; c++)
                {
                    var channel = sound.Channel(c);
                    for (var i = 0; i < n; i++) buffer[i * channels + c] = (float)channel[f + i];
                }
                sink.Write(buffer.AsSpan(0, n * channels));
            }
        }
    }
}

/// <summary>
/// AIFF and AIFC (Apple's Audio Interchange File Format, 1989; AIFF-C draft 1991): the COMM
/// chunk's channels, frames, sample size and 80-bit sample rate, then the SSND chunk's
/// interleaved samples, streamed to the sink. AIFC compression types NONE/twos (big-endian
/// PCM), sowt (little-endian PCM), fl32 and fl64 are read.
/// </summary>
public static class AiffDecoder
{
    public static void Decode(Stream stream, IPcmSink sink, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Tag(reader) != "FORM") throw new FormatException("Not an IFF file.");
        reader.ReadUInt32();
        var form = Tag(reader);
        if (form is not ("AIFF" or "AIFC")) throw new FormatException("Not an AIFF file.");

        int channels = 0, bits = 0;
        uint frames = 0;
        double rate = 0;
        var compression = "NONE";
        long? soundData = null; // where SSND's samples start, if it came before COMM
        uint soundBytes = 0;
        while (true)
        {
            string id;
            uint size;
            try
            {
                id = Tag(reader);
                size = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
            }
            catch (EndOfStreamException)
            {
                break;
            }
            var next = stream.CanSeek ? stream.Position + size + (size & 1) : -1;
            if (id == "COMM")
            {
                var comm = reader.ReadBytes((int)size);
                if (comm.Length < 18) throw new FormatException("Short AIFF COMM chunk.");
                channels = BinaryPrimitives.ReadInt16BigEndian(comm);
                frames = BinaryPrimitives.ReadUInt32BigEndian(comm.AsSpan(2));
                bits = BinaryPrimitives.ReadInt16BigEndian(comm.AsSpan(6));
                rate = Extended(comm.AsSpan(8, 10));
                if (form == "AIFC" && comm.Length >= 22) compression = Encoding.ASCII.GetString(comm, 18, 4);
                if ((size & 1) == 1) reader.ReadByte();
                if (soundData is { } at)
                {
                    stream.Position = at;
                    Samples(reader, sink, channels, bits, rate, compression, frames, soundBytes, progress, cancellationToken);
                    return;
                }
            }
            else if (id == "SSND")
            {
                var offset = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                reader.ReadUInt32(); // block size
                if (offset > 0) reader.ReadBytes((int)offset);
                var bytes = size - 8 - offset;
                if (channels == 0)
                {
                    if (!stream.CanSeek) throw new FormatException("AIFF sound data before its COMM chunk.");
                    soundData = stream.Position;
                    soundBytes = bytes;
                }
                else
                {
                    Samples(reader, sink, channels, bits, rate, compression, frames, bytes, progress, cancellationToken);
                    return;
                }
            }
            if (next >= 0) stream.Position = Math.Min(next, stream.Length);
            else if (id is not ("COMM" or "SSND")) reader.ReadBytes((int)(size + (size & 1)));
        }
        throw new FormatException("No AIFF sound data found.");
    }

    private static void Samples(BinaryReader reader, IPcmSink sink, int channels, int bits, double rate, string compression,
        uint frames, uint bytes, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (channels <= 0 || rate <= 0) throw new FormatException("AIFF without a usable format.");
        var (bytesPerSample, kind) = compression switch
        {
            "NONE" or "twos" => ((bits + 7) / 8, 'b'),
            "sowt" => ((bits + 7) / 8, 'l'),
            "fl32" or "FL32" => (4, 'f'),
            "fl64" or "FL64" => (8, 'f'),
            _ => throw new FormatException($"Compressed AIFF ('{compression}') isn't read here."),
        };
        if (kind != 'f' && bytesPerSample is < 1 or > 4) throw new FormatException($"Unsupported AIFF sample size {bits}.");
        sink.SetFormat((int)Math.Round(rate), channels);

        var frameBytes = bytesPerSample * channels;
        var total = Math.Min(frames, bytes / (uint)frameBytes);
        const int block = 8192;
        var raw = new byte[block * frameBytes];
        var samples = new float[block * channels];
        for (long done = 0; done < total;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var want = (int)Math.Min(block, total - done);
            var got = reader.Read(raw, 0, want * frameBytes) / frameBytes;
            if (got == 0) break; // a truncated file: keep what was there
            for (var i = 0; i < got * channels; i++)
            {
                var s = raw.AsSpan(i * bytesPerSample, bytesPerSample);
                samples[i] = (kind, bytesPerSample) switch
                {
                    ('f', 4) => BinaryPrimitives.ReadSingleBigEndian(s),
                    ('f', _) => (float)BinaryPrimitives.ReadDoubleBigEndian(s),
                    ('b', 1) => (sbyte)s[0] / 128f,
                    ('b', 2) => BinaryPrimitives.ReadInt16BigEndian(s) / 32768f,
                    ('b', 3) => ((s[0] << 24) | (s[1] << 16) | (s[2] << 8)) / 2147483648f,
                    ('b', _) => BinaryPrimitives.ReadInt32BigEndian(s) / 2147483648f,
                    (_, 1) => (sbyte)s[0] / 128f,
                    (_, 2) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (_, 3) => ((s[2] << 24) | (s[1] << 16) | (s[0] << 8)) / 2147483648f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                };
            }
            sink.Write(samples.AsSpan(0, got * channels));
            done += got;
            progress?.Report((double)done / total);
        }
    }

    /// <summary>An 80-bit IEEE 754 extended-precision number (big-endian), as AIFF stores its sample rate.</summary>
    internal static double Extended(ReadOnlySpan<byte> b)
    {
        var exponent = ((b[0] & 0x7F) << 8) | b[1];
        var mantissa = BinaryPrimitives.ReadUInt64BigEndian(b[2..]);
        if (exponent == 0 && mantissa == 0) return 0;
        var value = mantissa * Math.Pow(2, exponent - 16383 - 63);
        return (b[0] & 0x80) != 0 ? -value : value;
    }

    private static string Tag(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }
}
