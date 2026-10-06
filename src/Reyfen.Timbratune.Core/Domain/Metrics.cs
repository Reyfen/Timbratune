using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Domain;

// Port of dashboard-react/src/metrics.ts — the registry behind the
// click-to-expand reference modal. Each entry knows how to pull its value from
// a take and from a reference voice, plus the zones / scale it shares with the
// on-card ZoneBar.

public enum MetricKey { Pitch, Loudness, Sd, Hnr, Jitter, Weight, F2, F3 }

public sealed record MetricDef(
    MetricKey Key,
    string Title,
    string Unit,
    IReadOnlyList<Zone> Zones,
    double Lo,
    double Hi,
    Func<Recording, double?> Take,
    Func<ReferenceVoice, double?> Ref,
    // Whether real men's/women's reference ticks are meaningful (gendered cues).
    bool ShowRefs,
    // One-line framing shown in the modal header.
    string Blurb);

public static class Metrics
{
    public static readonly IReadOnlyDictionary<MetricKey, MetricDef> All = new Dictionary<MetricKey, MetricDef>
    {
        [MetricKey.Pitch] = new(MetricKey.Pitch, "Pitch (avg)", "Hz", Zones.Pitch, 100, 260,
            r => r.Pitch.MeanHz, v => v.Pitch.MeanHz, true,
            "where you sit vs. real men's & women's voices · 165 Hz+ reads feminine"),
        [MetricKey.Loudness] = new(MetricKey.Loudness, "Loudness", "dB", Zones.Loudness, 45, 78,
            r => r.Intensity.MeanDb, v => v.Intensity.MeanDb, false,
            "louder = more present · this one isn't gendered, just your takes here"),
        [MetricKey.Sd] = new(MetricKey.Sd, "Pitch variability", "Hz", Zones.PitchSd, 0, 60,
            r => r.Pitch.SdHz, v => v.Pitch.SdHz, false,
            "how much your melody moves · ~20–40 Hz is lively, natural speech"),
        [MetricKey.Hnr] = new(MetricKey.Hnr, "Clarity (HNR)", "dB", Zones.Hnr, 0, 30,
            r => r.VoiceQuality.HnrDb, v => v.VoiceQuality.HnrDb, false,
            "higher = clearer, lower = breathier · a skill cue, not a gender cue"),
        [MetricKey.Jitter] = new(MetricKey.Jitter, "Steadiness (jitter)", "%", Zones.Jitter, 0, 3,
            r => r.VoiceQuality.JitterPct, v => v.VoiceQuality.JitterPct, false,
            "lower = steadier · a skill cue, not a gender cue"),
        [MetricKey.Weight] = new(MetricKey.Weight, "Weight", "dB", Zones.Weight, 0, 20,
            r => r.Weight?.H1a3cDb, v => v.Weight.H1a3cDb, true,
            "source spectral tilt (corrected H1*–A3*) · lighter leans feminine — but weight doesn't gender-separate reliably, so watch your OWN change over time"),
        [MetricKey.F2] = new(MetricKey.F2, "Resonance (F2)", "Hz", Zones.F2, 1100, 2000,
            r => r.Formants.F2Hz, v => v.Formants?.F2Hz, true,
            "main brightness cue · median over vowel nuclei · higher = smaller, brighter tract (reads feminine)"),
        [MetricKey.F3] = new(MetricKey.F3, "Resonance (F3)", "Hz", Zones.F3, 2100, 3400,
            r => r.Formants.F3Hz, v => v.Formants?.F3Hz, true,
            "supports brightness · higher = brighter / lighter resonance"),
    };

    /// <summary>The 165 Hz "reads feminine" threshold used by trends and the contour chart.</summary>
    public const double FemininePitchHz = 165;
}
