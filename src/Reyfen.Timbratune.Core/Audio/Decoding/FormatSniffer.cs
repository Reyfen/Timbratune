using System.Buffers.Binary;
using System.Text;

namespace Reyfen.Timbratune.Core.Audio.Decoding;

/// <summary>
/// Tells an audio file's format from its first bytes: RIFF/RF64/BW64 WAVE, FORM AIFF/AIFC, fLaC,
/// ID3 tags and MPEG or ADTS frame sync, OggS with its codec header, the ISO base media
/// <c>ftyp</c> brand, EBML with its doc type, <c>caff</c>, ASF and <c>#!AMR</c>. The extension
/// is never trusted; it only picks between formats that share a container (an MP4 file named
/// .m4a is an M4A).
/// </summary>
public static class FormatSniffer
{
    /// <summary>How much of the file is looked at (after any ID3 tags).</summary>
    public const int HeadBytes = 64 * 1024;

    public static AudioFormat? Sniff(AudioSource source)
    {
        using var stream = source.Open();
        return Sniff(stream, source.Name);
    }

    /// <summary>Reads from the stream's current position.</summary>
    public static AudioFormat? Sniff(Stream stream, string? fileName = null)
    {
        var head = new byte[HeadBytes];
        var n = ReadAtMost(stream, head, 10);
        // ID3v2 tags (possibly several, possibly with large cover art) come before MP3, AAC or FLAC data.
        var tagged = false;
        while (n == 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3' && SyncSafe(head.AsSpan(6, 4)) is var size and >= 0)
        {
            tagged = true;
            var skip = size + ((head[5] & 0x10) != 0 ? 10 : 0);
            if (!Skip(stream, skip)) return null;
            n = ReadAtMost(stream, head, 10);
        }
        n += ReadAtMost(stream, head.AsSpan(n), head.Length - n);
        var format = Sniff(head.AsSpan(0, n), fileName, tagged);
        return format is null ? null : AudioFormatCatalog.Get(format);
    }

    /// <summary>The format id of a file starting with <paramref name="head"/>, or null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head, string? fileName = null, bool afterId3 = false)
    {
        if (head.Length >= 12 && Is(head, 8, "WAVE") && (Is(head, 0, "RIFF") || Is(head, 0, "RF64") || Is(head, 0, "BW64")))
            return AudioFormatCatalog.Wav;
        if (head.Length >= 12 && Is(head, 0, "FORM") && (Is(head, 8, "AIFF") || Is(head, 8, "AIFC")))
            return AudioFormatCatalog.Aiff;
        if (Is(head, 0, "fLaC")) return AudioFormatCatalog.Flac;
        if (Is(head, 0, "OggS")) return Ogg(head);
        if (head.Length >= 12 && Is(head, 4, "ftyp")) return IsoMedia(head, fileName);
        if (head.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(head) == 0x1A45DFA3) return Ebml(head);
        if (Is(head, 0, "caff")) return AudioFormatCatalog.Caf;
        if (head.StartsWith(AsfHeader)) return AudioFormatCatalog.Wma;
        if (Is(head, 0, "#!AMR")) return AudioFormatCatalog.Amr;
        return MpegOrAdts(head, afterId3);
    }

    private static readonly byte[] AsfHeader = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

    private static string? Ogg(ReadOnlySpan<byte> head)
    {
        // First page: 27-byte header, the segment table, then the first packet (the codec's id header).
        if (head.Length < 28) return null;
        var segments = head[26];
        var packet = head[Math.Min(head.Length, 27 + segments)..];
        if (Is(packet, 0, "OpusHead")) return AudioFormatCatalog.Opus;
        // Vorbis ("\x01vorbis"), and the rarer Ogg FLAC or Speex, which the general decoders read too.
        return AudioFormatCatalog.Ogg;
    }

    private static string IsoMedia(ReadOnlySpan<byte> head, string? fileName)
    {
        // The ftyp box: size, "ftyp", major brand, minor version, compatible brands.
        var boxSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head), (uint)head.Length);
        var brands = new List<string> { Ascii(head.Slice(8, 4)) };
        for (var o = 16; o + 4 <= boxSize; o += 4) brands.Add(Ascii(head.Slice(o, 4)));

        // The extension decides between the formats of this family; it can't make it something else.
        if (fileName is not null && AudioFormatCatalog.ByExtension(fileName) is { Id: AudioFormatCatalog.M4a or AudioFormatCatalog.Mp4 or AudioFormatCatalog.ThreeGp } byName)
            return byName.Id;
        var major = brands[0];
        if (major.StartsWith("3g", StringComparison.Ordinal)) return AudioFormatCatalog.ThreeGp;
        if (brands.Any(b => b is "M4A " or "M4B " or "M4P ")) return AudioFormatCatalog.M4a;
        return AudioFormatCatalog.Mp4;
    }

    private static string Ebml(ReadOnlySpan<byte> head)
    {
        // The DocType element (id 0x4282) in the EBML header says "webm" or "matroska".
        var header = head[..Math.Min(head.Length, 256)];
        var at = header.IndexOf((ReadOnlySpan<byte>)[0x42, 0x82]);
        return at >= 0 && header[(at + 2)..].IndexOf("webm"u8) is >= 0 and < 4 ? AudioFormatCatalog.WebM : AudioFormatCatalog.Mkv;
    }

    /// <summary>
    /// MPEG audio (MP3, and MP1/MP2) or AAC in ADTS: two frame headers in a row, the second where the
    /// first one's length says (or one, if it fills the rest of what was read). Leading zeros (padding
    /// after tags) are skipped; without tags the first frame must start the file.
    /// </summary>
    private static string? MpegOrAdts(ReadOnlySpan<byte> head, bool afterId3)
    {
        var start = 0;
        if (afterId3)
            while (start < head.Length && head[start] == 0) start++;
        // A little slack for junk before the first frame (common after tags, rare without).
        var last = Math.Min(head.Length - 4, start + (afterId3 ? 4096 : 0));
        for (var o = start; o <= last; o++)
        {
            if (head[o] != 0xFF) continue;
            if (AdtsLength(head[o..]) is > 0 and var adts && Confirms(head, o, adts, AdtsLength)) return AudioFormatCatalog.Aac;
            if (MpegLength(head[o..]) is > 0 and var mpeg && Confirms(head, o, mpeg, MpegLength)) return AudioFormatCatalog.Mp3;
        }
        return null;
    }

    private delegate int FrameLength(ReadOnlySpan<byte> frame);

    private static bool Confirms(ReadOnlySpan<byte> head, int offset, int length, FrameLength next)
    {
        var o = offset + length;
        // The first frame ends the file, or runs past what was read (only in a long file).
        if (o + 4 > head.Length) return o == head.Length || head.Length >= HeadBytes;
        return next(head[o..]) > 0;
    }

    private static readonly int[] MpegRatesV1 = [44100, 48000, 32000];
    private static readonly int[][] Kbps =
    [
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448], // V1 layer I
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],    // V1 layer II
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],     // V1 layer III
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],    // V2/2.5 layer I
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],         // V2/2.5 layers II, III
    ];

    /// <summary>The length of the MPEG audio frame starting here, or 0 if this isn't a valid header (free bitrate isn't accepted).</summary>
    private static int MpegLength(ReadOnlySpan<byte> h)
    {
        if (h.Length < 4 || h[0] != 0xFF || (h[1] & 0xE0) != 0xE0) return 0;
        var version = (h[1] >> 3) & 3; // 0 = 2.5, 2 = 2, 3 = 1
        var layer = (h[1] >> 1) & 3;   // 1 = III, 2 = II, 3 = I
        var bitrate = h[2] >> 4;
        var rateIndex = (h[2] >> 2) & 3;
        if (version == 1 || layer == 0 || bitrate is 0 or 15 || rateIndex == 3) return 0;
        var rate = MpegRatesV1[rateIndex] >> (version == 3 ? 0 : version == 2 ? 1 : 2);
        var padding = (h[2] >> 1) & 1;
        var v1 = version == 3;
        var kbps = Kbps[v1 ? 3 - layer : layer == 3 ? 3 : 4][bitrate] * 1000;
        return layer switch
        {
            3 => (12 * kbps / rate + padding) * 4,
            2 => 144 * kbps / rate + padding,
            _ => (v1 ? 144 : 72) * kbps / rate + padding,
        };
    }

    /// <summary>The length of the ADTS frame starting here, or 0 if this isn't a valid ADTS header.</summary>
    private static int AdtsLength(ReadOnlySpan<byte> h)
    {
        if (h.Length < 7 || h[0] != 0xFF || (h[1] & 0xF6) != 0xF0) return 0;
        if (((h[2] >> 2) & 0xF) >= 13) return 0; // sampling frequency index
        var length = ((h[3] & 3) << 11) | (h[4] << 3) | (h[5] >> 5);
        return length >= 7 ? length : 0;
    }

    private static bool Is(ReadOnlySpan<byte> data, int offset, string ascii)
    {
        if (data.Length < offset + ascii.Length) return false;
        for (var i = 0; i < ascii.Length; i++)
            if (data[offset + i] != ascii[i]) return false;
        return true;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);

    private static int SyncSafe(ReadOnlySpan<byte> b) =>
        (b[0] | b[1] | b[2] | b[3]) >= 0x80 ? -1 : (b[0] << 21) | (b[1] << 14) | (b[2] << 7) | b[3];

    private static int ReadAtMost(Stream stream, Span<byte> buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = stream.Read(buffer.Slice(total, count - total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static int ReadAtMost(Stream stream, byte[] buffer, int count) => ReadAtMost(stream, buffer.AsSpan(), count);

    private static bool Skip(Stream stream, long count)
    {
        if (stream.CanSeek)
        {
            if (stream.Position + count > stream.Length) return false;
            stream.Seek(count, SeekOrigin.Current);
            return true;
        }
        Span<byte> scratch = stackalloc byte[4096];
        while (count > 0)
        {
            var n = stream.Read(scratch[..(int)Math.Min(count, scratch.Length)]);
            if (n == 0) return false;
            count -= n;
        }
        return true;
    }
}
