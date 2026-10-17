using Reyfen.Timbratune.Core.Audio.Decoding;
using SoundFlow.Backends.MiniAudio;

namespace Reyfen.Timbratune.Audio.SoundFlow;

/// <summary>
/// MP3, FLAC and WAV through miniaudio, on every native head. miniaudio is asked for 44.1 kHz
/// mono itself (its own resampler and downmix), so these imports decode exactly as they always
/// have; the sink passes that through unchanged.
/// </summary>
/// <param name="audio">The app's miniaudio engine, only fetched when a file is decoded.</param>
public sealed class MiniaudioDecoderProvider(Func<SoundFlowAudio> audio) : IAudioDecoderProvider
{
    public const string ProviderId = "miniaudio";

    private static readonly FormatSet Formats = new([AudioFormatCatalog.Mp3, AudioFormatCatalog.Flac, AudioFormatCatalog.Wav]);

    public string Id => ProviderId;

    public int Priority => 2;

    public ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default) => new(Formats);

    public IAudioDecoder Create() => new Decoder(audio().Engine);

    private sealed class Decoder(MiniAudioEngine engine) : IAudioDecoder
    {
        public Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.Run(() =>
            {
                // Streaming: the whole file is never held in memory.
                using var stream = source.Open();
                using var decoder = engine.CreateDecoder(stream, out _, SoundFlowAudio.Format(1));
                sink.SetFormat(SoundFlowAudio.SampleRate, 1);
                var buffer = new float[SoundFlowAudio.SampleRate];
                var total = 0L;
                int read;
                while ((read = decoder.Decode(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sink.Write(buffer.AsSpan(0, read));
                    total += read;
                    if (stream.CanSeek && stream.Length > 0) progress?.Report((double)stream.Position / stream.Length);
                }
                if (total == 0) throw new InvalidDataException($"No audio could be decoded from {source.Name}.");
            }, cancellationToken);

        public void Dispose()
        {
        }
    }
}
