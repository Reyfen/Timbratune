using Android.Media;
using Reyfen.Timbratune.Core.Audio.Decoding;
using AudioSource = Reyfen.Timbratune.Core.Audio.Decoding.AudioSource;

namespace Reyfen.Timbratune.Audio.Android;

/// <summary>
/// Android's own decoders: MediaExtractor reads the container (MP4/M4A/3GP, ADTS, AMR, Ogg,
/// Matroska/WebM, MP3, FLAC), MediaCodec decodes the first audio track. The probe asks
/// MediaCodecList which audio decoders this phone has.
/// </summary>
public sealed class MediaCodecDecoderProvider : IAudioDecoderProvider
{
    public const string ProviderId = "mediacodec";

    /// <summary>Which decoders (by MIME type; any one of them) make which container format readable.</summary>
    private static readonly (string Format, string[] Codecs)[] Needs =
    [
        (AudioFormatCatalog.M4a, [MediaFormat.MimetypeAudioAac]),
        (AudioFormatCatalog.Mp4, [MediaFormat.MimetypeAudioAac]),
        (AudioFormatCatalog.Aac, [MediaFormat.MimetypeAudioAac]),
        (AudioFormatCatalog.ThreeGp, [MediaFormat.MimetypeAudioAac, MediaFormat.MimetypeAudioAmrNb]),
        (AudioFormatCatalog.Amr, [MediaFormat.MimetypeAudioAmrNb, MediaFormat.MimetypeAudioAmrWb]),
        (AudioFormatCatalog.Ogg, [MediaFormat.MimetypeAudioVorbis]),
        (AudioFormatCatalog.Opus, [MediaFormat.MimetypeAudioOpus]),
        (AudioFormatCatalog.WebM, [MediaFormat.MimetypeAudioOpus, MediaFormat.MimetypeAudioVorbis]),
        (AudioFormatCatalog.Mkv, [MediaFormat.MimetypeAudioAac, MediaFormat.MimetypeAudioOpus, MediaFormat.MimetypeAudioVorbis]),
        (AudioFormatCatalog.Mp3, [MediaFormat.MimetypeAudioMpeg]),
        (AudioFormatCatalog.Flac, [MediaFormat.MimetypeAudioFlac]),
    ];

    public string Id => ProviderId;

    public int Priority => 4;

    public ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default) => new(Task.Run(() =>
    {
        var codecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var list = new MediaCodecList(MediaCodecListKind.RegularCodecs);
        foreach (var info in list.GetCodecInfos())
            if (!info.IsEncoder)
                foreach (var type in info.GetSupportedTypes())
                    codecs.Add(type);
        return new FormatSet(Needs.Where(n => n.Codecs.Any(codecs.Contains)).Select(n => n.Format));
    }, cancellationToken));

    public IAudioDecoder Create() => new MediaCodecDecoder();
}

/// <summary>Decodes a file's first audio track with the synchronous MediaCodec API, to PCM at its own rate and channels.</summary>
internal sealed class MediaCodecDecoder : IAudioDecoder
{
    private const long TimeoutUs = 10_000;

    public Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var path = source.Path;
            string? copy = null;
            if (path is null)
            {
                copy = Path.Combine(Path.GetTempPath(), $"timbratune-mc-{Guid.NewGuid():N}.{source.Format?.Extension ?? "bin"}");
                using (var input = source.Open())
                using (var output = File.Create(copy))
                    input.CopyTo(output);
                path = copy;
            }
            try
            {
                if (Decode(path, sink, progress, cancellationToken) == 0)
                    throw new InvalidDataException($"No audio could be decoded from {source.Name}.");
            }
            finally
            {
                if (copy is not null) try { File.Delete(copy); } catch (IOException) { }
            }
        }, cancellationToken);

    public void Dispose()
    {
    }

    private static long Decode(string path, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var extractor = new MediaExtractor();
        extractor.SetDataSource(path);
        var track = -1;
        MediaFormat? format = null;
        for (var i = 0; i < extractor.TrackCount && track < 0; i++)
        {
            var candidate = extractor.GetTrackFormat(i);
            if (candidate.GetString(MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true)
            {
                track = i;
                format = candidate;
            }
            else candidate.Dispose();
        }
        if (format is null) throw new InvalidDataException("No audio track.");
        using var trackFormat = format;
        extractor.SelectTrack(track);
        var mime = format.GetString(MediaFormat.KeyMime)!;
        var durationUs = format.ContainsKey(MediaFormat.KeyDuration) ? format.GetLong(MediaFormat.KeyDuration) : 0;

        using var codec = MediaCodec.CreateDecoderByType(mime);
        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();
        try
        {
            using var info = new MediaCodec.BufferInfo();
            int rate = 0, channels = 0;
            var encoding = Encoding.Pcm16bit;
            var formatSet = false;
            var inputDone = false;
            byte[] bytes = [];
            float[] samples = [];
            long frames = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!inputDone)
                {
                    var input = codec.DequeueInputBuffer(TimeoutUs);
                    if (input >= 0)
                    {
                        var buffer = codec.GetInputBuffer(input)!;
                        var size = extractor.ReadSampleData(buffer, 0);
                        if (size < 0)
                        {
                            codec.QueueInputBuffer(input, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            codec.QueueInputBuffer(input, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None);
                            extractor.Advance();
                        }
                    }
                }

                var output = codec.DequeueOutputBuffer(info, TimeoutUs);
                if (output == (int)MediaCodecInfoState.OutputFormatChanged)
                {
                    using var changed = codec.OutputFormat;
                    var (newRate, newChannels) = (changed.GetInteger(MediaFormat.KeySampleRate), changed.GetInteger(MediaFormat.KeyChannelCount));
                    if (formatSet && (newRate, newChannels) != (rate, channels)) throw new InvalidDataException("The audio format changes mid-file.");
                    (rate, channels) = (newRate, newChannels);
                    if (changed.ContainsKey(MediaFormat.KeyPcmEncoding)) encoding = (Encoding)changed.GetInteger(MediaFormat.KeyPcmEncoding);
                    continue;
                }
                if (output < 0) continue; // nothing yet (or the deprecated "buffers changed")
                try
                {
                    if (info.Size > 0)
                    {
                        if (rate == 0 || channels == 0)
                        {
                            using var current = codec.OutputFormat;
                            (rate, channels) = (current.GetInteger(MediaFormat.KeySampleRate), current.GetInteger(MediaFormat.KeyChannelCount));
                        }
                        if (!formatSet) sink.SetFormat(rate, channels);
                        formatSet = true;
                        var buffer = codec.GetOutputBuffer(output)!;
                        if (bytes.Length < info.Size) bytes = new byte[info.Size];
                        buffer.Position(info.Offset);
                        buffer.Get(bytes, 0, info.Size);
                        var count = encoding == Encoding.PcmFloat ? info.Size / 4 : info.Size / 2;
                        count -= count % channels;
                        if (samples.Length < count) samples = new float[count];
                        if (encoding == Encoding.PcmFloat) Buffer.BlockCopy(bytes, 0, samples, 0, count * 4);
                        else
                            for (var i = 0; i < count; i++)
                                samples[i] = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 32768f;
                        if (count > 0) sink.Write(samples.AsSpan(0, count));
                        frames += count / channels;
                        if (durationUs > 0) progress?.Report(Math.Clamp((double)info.PresentationTimeUs / durationUs, 0, 1));
                    }
                }
                finally
                {
                    codec.ReleaseOutputBuffer(output, false);
                }
                if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) return frames;
            }
        }
        finally
        {
            codec.Stop();
            codec.Release();
        }
    }
}
