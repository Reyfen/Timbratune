namespace Reyfen.Timbratune.Core.Audio.Decoding;

/// <summary>What a decode used: the file's format, the provider that read it, the frames written.</summary>
public sealed record DecodedAudio(AudioFormat Format, string ProviderId, long Frames);

/// <summary>
/// All the decoders of this device. Probes each provider once (in the background, when first
/// asked) and keeps what they read; <see cref="RefreshAsync"/> probes again, e.g. after FFmpeg
/// was installed, and raises <see cref="CapabilitiesChanged"/>. A decode sniffs the file's
/// format, then tries the providers that read it, by priority, until one succeeds.
/// </summary>
public sealed class DecoderRegistry
{
    private readonly IReadOnlyList<IAudioDecoderProvider> _providers;
    private readonly object _gate = new();
    private Task<IReadOnlyDictionary<string, FormatSet>>? _probe;

    public DecoderRegistry(IEnumerable<IAudioDecoderProvider> providers) =>
        _providers = providers.OrderBy(p => p.Priority).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();

    /// <summary>By priority, first first.</summary>
    public IReadOnlyList<IAudioDecoderProvider> Providers => _providers;

    /// <summary>Raised (on the probing thread) when a re-probe finished.</summary>
    public event EventHandler? CapabilitiesChanged;

    /// <summary>Every format some decoder here reads.</summary>
    public async Task<FormatSet> GetFormatsAsync()
    {
        var sets = await Probed().ConfigureAwait(false);
        return sets.Values.Aggregate(FormatSet.Empty, (all, set) => all.Union(set));
    }

    /// <summary>What one provider reads (empty if it isn't registered or its probe failed).</summary>
    public async Task<FormatSet> GetFormatsAsync(string providerId) =>
        (await Probed().ConfigureAwait(false)).GetValueOrDefault(providerId) ?? FormatSet.Empty;

    /// <summary>Probes every provider again (e.g. after something was installed); the next pickers see the result.</summary>
    public async Task<FormatSet> RefreshAsync()
    {
        lock (_gate) _probe = null;
        var formats = await GetFormatsAsync().ConfigureAwait(false);
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        return formats;
    }

    /// <summary>
    /// Decodes <paramref name="source"/> into a 44.1 kHz mono PCM16 WAV written to <paramref name="target"/>
    /// (seekable; rewritten from the start for each decoder tried).
    /// </summary>
    /// <exception cref="UnsupportedAudioException">The format is unknown, or nothing here reads it.</exception>
    /// <exception cref="InvalidDataException">Every decoder that reads the format failed on this file (the last failure is inside).</exception>
    public async Task<DecodedAudio> DecodeAsync(AudioSource source, Stream target, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var format = await Task.Run(() => FormatSniffer.Sniff(source), cancellationToken).ConfigureAwait(false)
                     ?? throw new UnsupportedAudioException(source.Name, null);
        var sets = await Probed().ConfigureAwait(false);
        var candidates = _providers.Where(p => sets.GetValueOrDefault(p.Id)?.Contains(format.Id) == true).ToList();
        if (candidates.Count == 0) throw new UnsupportedAudioException(source.Name, format);
        source = source.WithFormat(format);

        Exception? failure = null;
        foreach (var provider in candidates)
        {
            target.SetLength(0);
            target.Position = 0;
            try
            {
                using var decoder = provider.Create();
                using var sink = new AnalysisWavSink(target, leaveOpen: true);
                await decoder.DecodeAsync(source, sink, progress, cancellationToken).ConfigureAwait(false);
                sink.Complete();
                return new DecodedAudio(format, provider.Id, sink.Frames);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failure = e; // the next one may read it
            }
        }
        // E.g. a Matroska file whose codec none of them has, or a damaged file.
        throw new InvalidDataException($"its {format.DisplayName} audio couldn't be decoded ({failure!.Message.TrimEnd('.')})", failure);
    }

    private Task<IReadOnlyDictionary<string, FormatSet>> Probed()
    {
        lock (_gate) return _probe ??= Task.Run(ProbeAllAsync);
    }

    private async Task<IReadOnlyDictionary<string, FormatSet>> ProbeAllAsync()
    {
        var sets = new Dictionary<string, FormatSet>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            try
            {
                sets[provider.Id] = await provider.ProbeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                sets[provider.Id] = FormatSet.Empty; // a provider that can't tell decodes nothing
            }
        }
        return sets;
    }
}
