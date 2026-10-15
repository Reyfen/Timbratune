using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Reyfen.Timbratune.Core.Audio.Decoding;

namespace Reyfen.Timbratune.Audio.MediaFoundation;

/// <summary>
/// Windows' own decoders, through the Media Foundation Source Reader: AAC/M4A, ALAC, WMA, AMR,
/// the audio of MP4/MOV/3GP/MKV videos, and Ogg/Opus/WebM where the Store's Web Media
/// Extensions are installed. The probe decodes a tiny clip of each candidate format once, so it
/// reports what this installation reads (Store codecs don't show in the registry).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationDecoderProvider : IAudioDecoderProvider
{
    public const string ProviderId = "mediafoundation";

    /// <summary>Which embedded clip proves which format.</summary>
    private static readonly (string Format, string Clip)[] Candidates =
    [
        (AudioFormatCatalog.M4a, "probe.m4a"), (AudioFormatCatalog.Mp4, "probe.mp4"), (AudioFormatCatalog.ThreeGp, "probe.3gp"),
        (AudioFormatCatalog.Aac, "probe.aac"), (AudioFormatCatalog.Wma, "probe.wma"), (AudioFormatCatalog.Amr, "probe.amr"),
        (AudioFormatCatalog.Ogg, "probe.ogg"), (AudioFormatCatalog.Opus, "probe.opus"), (AudioFormatCatalog.WebM, "probe.webm"),
        (AudioFormatCatalog.Mkv, "probe.mka"), (AudioFormatCatalog.Flac, "probe.flac"), (AudioFormatCatalog.Mp3, "probe.mp3"),
        (AudioFormatCatalog.Caf, "probe.caf"),
    ];

    public string Id => ProviderId;

    public int Priority => 3;

    /// <summary>How long the last probe took (for the log and the bench).</summary>
    public TimeSpan LastProbeTime { get; private set; }

    public ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default) =>
        new(Task.Run(() => Probe(cancellationToken), cancellationToken));

    public IAudioDecoder Create() => new MediaFoundationDecoder();

    private FormatSet Probe(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var found = new List<string>();
        var folder = Path.Combine(Path.GetTempPath(), "timbratune-mf-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var decoder = new MediaFoundationDecoder();
            foreach (var (format, clip) in Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(folder, clip);
                using (var resource = typeof(MediaFoundationDecoderProvider).Assembly.GetManifestResourceStream(clip)!)
                using (var file = File.Create(path))
                    resource.CopyTo(file);
                if (decoder.CanDecode(path)) found.Add(format);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            // No Media Foundation (Windows N without the Media Feature Pack, Server Core): nothing here.
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            LastProbeTime = watch.Elapsed;
        }
        return new FormatSet(found);
    }
}

/// <summary>One Media Foundation session (MFStartup … MFShutdown) decoding files to float PCM.</summary>
[SupportedOSPlatform("windows")]
internal sealed class MediaFoundationDecoder : IAudioDecoder
{
    private bool _started;

    public MediaFoundationDecoder()
    {
        Mf.Check(Mf.MFStartup(Mf.Version, Mf.StartupLite));
        _started = true;
    }

    public Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // The Source Reader picks its parser by the file's extension: give it one that matches the content.
            var format = source.Format;
            var path = source.Path;
            string? copy = null;
            if (path is null || format is not null && !format.Extensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()))
            {
                copy = Path.Combine(Path.GetTempPath(), $"timbratune-mf-{Guid.NewGuid():N}.{format?.Extension ?? "bin"}");
                using (var input = source.Open())
                using (var output = File.Create(copy))
                    input.CopyTo(output);
                path = copy;
            }
            try
            {
                if (Decode(path, sink, progress, cancellationToken, firstBlockOnly: false) == 0)
                    throw new InvalidDataException($"No audio could be decoded from {source.Name}.");
            }
            finally
            {
                if (copy is not null) try { File.Delete(copy); } catch (IOException) { }
            }
        }, cancellationToken);

    /// <summary>Whether the first audio of <paramref name="path"/> decodes (for the probe).</summary>
    public bool CanDecode(string path)
    {
        try
        {
            return Decode(path, new NullSink(), null, CancellationToken.None, firstBlockOnly: true) > 0;
        }
        catch (Exception e) when (e is COMException or InvalidDataException or InvalidCastException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        Mf.MFShutdown();
    }

    /// <summary>Decodes the first audio stream as float at its own rate and channels; returns the frames written.</summary>
    private static unsafe long Decode(string path, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken, bool firstBlockOnly)
    {
        Mf.Check(Mf.MFCreateSourceReaderFromURL(path, null, out var reader));
        IMFMediaType? request = null;
        try
        {
            Mf.Check(reader.SetStreamSelection(Mf.AllStreams, false));
            Mf.Check(reader.SetStreamSelection(Mf.FirstAudioStream, true));
            Mf.Check(Mf.MFCreateMediaType(out request));
            Mf.Check(request.SetGUID(Mf.MajorType, Mf.Audio));
            Mf.Check(request.SetGUID(Mf.Subtype, Mf.Float));
            Mf.Check(reader.SetCurrentMediaType(Mf.FirstAudioStream, 0, request));
            var (rate, channels) = CurrentFormat(reader);

            var duration = 0L; // 100 ns units
            var value = default(PropVariant);
            if (reader.GetPresentationAttribute(Mf.MediaSource, Mf.Duration, &value) >= 0 && value.Vt == Mf.VtUi8) duration = (long)value.Value;

            long frames = 0;
            var formatSet = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mf.Check(reader.ReadSample(Mf.FirstAudioStream, 0, out _, out var flags, out var time, out var sample));
                if ((flags & Mf.CurrentMediaTypeChanged) != 0)
                {
                    // AAC decoders often settle the real rate or channels on the first sample
                    // (HE-AAC, mono signalled as stereo); a change after audio was written can't be followed.
                    var changed = CurrentFormat(reader);
                    if (formatSet && changed != (rate, channels))
                    {
                        Mf.Release(sample);
                        throw new InvalidDataException("The audio format changes mid-file.");
                    }
                    (rate, channels) = changed;
                }
                if (sample is not null)
                {
                    if (!formatSet) sink.SetFormat((int)rate, (int)channels);
                    formatSet = true;
                    try
                    {
                        Mf.Check(sample.ConvertToContiguousBuffer(out var buffer));
                        try
                        {
                            Mf.Check(buffer.Lock(out var data, out _, out var length));
                            try
                            {
                                var count = (int)(length / sizeof(float) / channels * channels);
                                if (count > 0) sink.Write(new ReadOnlySpan<float>(data, count));
                                frames += count / channels;
                            }
                            finally
                            {
                                buffer.Unlock();
                            }
                        }
                        finally
                        {
                            Mf.Release(buffer);
                        }
                    }
                    finally
                    {
                        Mf.Release(sample);
                    }
                    if (duration > 0) progress?.Report(Math.Clamp((double)time / duration, 0, 1));
                    if (firstBlockOnly && frames > 0) return frames;
                }
                if ((flags & Mf.EndOfStream) != 0) return frames;
            }
        }
        finally
        {
            Mf.Release(request);
            Mf.Release(reader);
        }
    }

    private static (uint Rate, uint Channels) CurrentFormat(IMFSourceReader reader)
    {
        Mf.Check(reader.GetCurrentMediaType(Mf.FirstAudioStream, out var type));
        try
        {
            Mf.Check(type.GetUINT32(Mf.SamplesPerSecond, out var rate));
            Mf.Check(type.GetUINT32(Mf.Channels, out var channels));
            if (rate == 0 || channels == 0) throw new InvalidDataException("No audio format.");
            return (rate, channels);
        }
        finally
        {
            Mf.Release(type);
        }
    }

    private sealed class NullSink : IPcmSink
    {
        public void SetFormat(int sampleRate, int channels) { }
        public void Write(ReadOnlySpan<float> interleaved) { }
    }
}
