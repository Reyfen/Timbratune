namespace Reyfen.Timbratune.Core.Analysis.Tracks;

/// <summary>
/// Which tracks an update computes (REFACTOR-PLAN §2.1). Tracks are shared measurements of a
/// take, computed once per update in fixed stages:
/// <list type="number">
/// <item>pitch, intensity, harmonicity, formants and LTAS, in parallel;</item>
/// <item>pulses, voiced frames, sounding stretches and the loud-vowel gate;</item>
/// <item>the derived tracks: vowel formant rows and weight frames.</item>
/// </list>
/// </summary>
[Flags]
public enum TrackSet
{
    None = 0,
    Pitch = 1 << 0,
    Intensity = 1 << 1,
    Harmonicity = 1 << 2,
    /// <summary>Both formant ceilings (5500 and 5000 Hz), from one shared resample.</summary>
    Formants = 1 << 3,
    /// <summary>Long-term average spectrum (live: only on the final update).</summary>
    Ltas = 1 << 4,
    /// <summary>Glottal pulses, with jitter per voiced stretch; needs pitch.</summary>
    Pulses = 1 << 5,
    /// <summary>The voiced pitch frames (time, F0).</summary>
    VoicedFrames = 1 << 6,
    /// <summary>Sounding stretches: silence detection on the intensity (−25 dB).</summary>
    Sounding = 1 << 7,
    /// <summary>The voiced frames within 10 dB of the loudness reference.</summary>
    LoudVowelGate = 1 << 8,
    /// <summary>Derived: the vowel-core formant rows (≤ 300 gated times, F1 in 250–1000 Hz, both ceilings).</summary>
    VowelFormantRows = 1 << 9,
    /// <summary>Derived: weight rows (H1, A3, formants, bandwidths) of ≤ 250 voiced frames.</summary>
    WeightFrames = 1 << 10,

    /// <summary>What a quick live update needs: the graphs (series, contour, phrases).</summary>
    Live = Pitch | Intensity | Harmonicity | Formants | Pulses | VoicedFrames | Sounding | LoudVowelGate,
    /// <summary>Everything except the LTAS: a full live update before the end.</summary>
    LiveFull = Live | VowelFormantRows | WeightFrames,
    All = LiveFull | Ltas,
}

public static class TrackSetExtensions
{
    /// <summary><paramref name="set"/> plus every track it is computed from.</summary>
    public static TrackSet WithDependencies(this TrackSet set)
    {
        if ((set & TrackSet.VowelFormantRows) != 0) set |= TrackSet.LoudVowelGate | TrackSet.Formants;
        if ((set & TrackSet.WeightFrames) != 0) set |= TrackSet.VoicedFrames | TrackSet.Formants;
        if ((set & TrackSet.LoudVowelGate) != 0) set |= TrackSet.VoicedFrames | TrackSet.Intensity;
        if ((set & (TrackSet.Pulses | TrackSet.VoicedFrames)) != 0) set |= TrackSet.Pitch;
        if ((set & TrackSet.Sounding) != 0) set |= TrackSet.Intensity;
        return set;
    }

    public static bool Has(this TrackSet set, TrackSet tracks) => (set & tracks) == tracks;
}
