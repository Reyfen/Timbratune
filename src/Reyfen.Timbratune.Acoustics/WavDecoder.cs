using System.Buffers.Binary;
using System.Text;

namespace Reyfen.Timbratune.Acoustics;

/// <summary>
/// Decodes RIFF/WAVE data (PCM 8/16/24/32-bit, IEEE float 32/64-bit, incl.
/// WAVE_FORMAT_EXTENSIBLE; also RF64/BW64 for files over 4 GB) into a <see cref="Sound"/>.
/// Compressed WAVE data (ADPCM, µ-law, MP3 in WAV…) is refused with a <see cref="FormatException"/>. Integer samples are
/// scaled to −1..1 by 2^(bits−1) (8-bit unsigned is re-centred first).
/// Works on a stream, so callers decide where the bytes come from.
/// </summary>
public static class WavDecoder
{
    private const ushort Pcm = 1, IeeeFloat = 3, Extensible = 0xFFFE;

    public static Sound Decode(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var riff = Tag(reader);
        if (riff is not ("RIFF" or "RF64" or "BW64")) throw new FormatException("Not a RIFF file.");
        reader.ReadUInt32();
        if (Tag(reader) != "WAVE") throw new FormatException("Not a WAVE file.");

        ushort format = 0, channels = 0, bits = 0;
        uint rate = 0;
        long? ds64DataSize = null; // RF64: the data chunk's real size, when its own field says 0xFFFFFFFF
        while (true)
        {
            string id;
            uint size;
            try
            {
                id = Tag(reader);
                size = reader.ReadUInt32();
            }
            catch (EndOfStreamException)
            {
                throw new FormatException("No audio data found.");
            }

            if (id == "fmt ")
            {
                var fmt = reader.ReadBytes((int)size);
                format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14));
                if (format == Extensible && fmt.Length >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24));
                if ((size & 1) == 1) reader.ReadByte();
            }
            else if (id == "ds64" && riff != "RIFF")
            {
                var ds64 = reader.ReadBytes((int)size);
                if (ds64.Length >= 16) ds64DataSize = BinaryPrimitives.ReadInt64LittleEndian(ds64.AsSpan(8));
                if ((size & 1) == 1) reader.ReadByte();
            }
            else if (id == "data")
            {
                if (channels == 0 || rate == 0) throw new FormatException("WAVE data before its format chunk.");
                if (format is not (Pcm or IeeeFloat)) throw new FormatException($"Compressed WAVE data (format {format}) isn't read here.");
                var bytesPerSample = (bits + 7) / 8;
                var length = size == uint.MaxValue && ds64DataSize is { } big ? big : size;
                var available = stream.CanSeek ? Math.Min(length, stream.Length - stream.Position) : length;
                var frames = (int)(available / (channels * (uint)bytesPerSample));
                var data = reader.ReadBytes(frames * channels * bytesPerSample);
                return new Sound(Deinterleave(data, frames, channels, bytesPerSample, format), rate);
            }
            else
            {
                reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
    }

    private static double[][] Deinterleave(byte[] data, int frames, int channels, int bytesPerSample, ushort format)
    {
        var result = new double[channels][];
        for (var c = 0; c < channels; c++) result[c] = new double[frames];
        var span = data.AsSpan();
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var s = span.Slice((i * channels + c) * bytesPerSample, bytesPerSample);
                result[c][i] = (format, bytesPerSample) switch
                {
                    (IeeeFloat, 4) => BinaryPrimitives.ReadSingleLittleEndian(s),
                    (IeeeFloat, 8) => BinaryPrimitives.ReadDoubleLittleEndian(s),
                    (_, 1) => s[0] / 128.0 - 1.0,
                    (_, 2) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768.0,
                    (_, 3) => ((s[0] | (s[1] << 8) | (s[2] << 16)) << 8 >> 8) / 8388608.0,
                    (_, 4) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648.0,
                    _ => throw new FormatException($"Unsupported WAVE sample format {format} with {bytesPerSample * 8} bits."),
                };
            }
        }
        return result;
    }

    private static string Tag(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }
}
