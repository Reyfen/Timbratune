namespace Reyfen.Timbratune.Core.Models;

// Mirrors analysis/<id>.json (RecordingDetail in dashboard-react/src/types.ts).

public sealed class RecordingDetail
{
    public double RegisterFloorHz { get; set; }
    public double SemitoneRefHz { get; set; } = 100.0;
    public double DurationS { get; set; }
    public double TimeStep { get; set; } = 0.01;
    public Frames Frames { get; set; } = new();
    public List<Phrase> Phrases { get; set; } = [];
    public Register Summary { get; set; } = new();

    /// <summary>
    /// Per-phrase metrics for the "trends within this take" charts, aligned
    /// with <see cref="Phrases"/>. C#-port addition — absent in files written
    /// by analyze.py / older versions (the pitch-based values can then be
    /// recomputed from <see cref="Frames"/>; F2 and weight can't).
    /// </summary>
    public List<PhraseMetrics>? PhraseMetrics { get; set; }

    /// <summary>
    /// The "trends within this take" charts: the take cut into equal slices of
    /// whole seconds (about 10 of them). C#-port addition — absent in older
    /// files; the pitch-based values can then be rebuilt from <see cref="Frames"/>.
    /// </summary>
    public TakeTrends? Trends { get; set; }
}

public sealed class TakeTrends
{
    /// <summary>What these trends hold; older versions are re-analyzed once to fill in the newer measures.</summary>
    public const int CurrentVersion = 2;

    /// <summary>1 (or absent): pitch, melody, endings, F2, F3, weight, HNR, jitter. 2: + loudness, pitch variability.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Slice length in whole seconds; the points sit at Step, 2·Step, 3·Step, …</summary>
    public double StepS { get; set; }
    public List<TrendSlice> Points { get; set; } = [];
}

/// <summary>
/// One trend point: the metrics of the slice around <see cref="T"/>
/// ([T − Step/2, T + Step/2], the first and last stretched to the take's edges).
/// Null = not measurable in that slice.
/// </summary>
public sealed class TrendSlice
{
    public double T { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    /// <summary>Mean F0 over the voiced frames.</summary>
    public double? MeanHz { get; set; }
    /// <summary>Sample SD of F0 over the voiced frames (pitch variability).</summary>
    public double? PitchSdHz { get; set; }
    /// <summary>Semitone SD over the in-register frames ("true melody").</summary>
    public double? MelodySt { get; set; }
    /// <summary>Energy average of the intensity frames, as the loudness card averages the take.</summary>
    public double? LoudnessDb { get; set; }
    /// <summary>Mean ending pitch of the phrases that end in the slice.</summary>
    public double? OffsetHz { get; set; }
    /// <summary>Median F2 / F3 of the gated vowel-core frames.</summary>
    public double? F2Hz { get; set; }
    public double? F3Hz { get; set; }
    /// <summary>Mean corrected H1*–A3* of the weight frames.</summary>
    public double? WeightDb { get; set; }
    /// <summary>Mean HNR over the voiced harmonicity frames.</summary>
    public double? HnrDb { get; set; }
    /// <summary>Local jitter (%) of the glottal pulses in the slice.</summary>
    public double? JitterPct { get; set; }
}

public sealed class PhraseMetrics
{
    public double Start { get; set; }
    public double End { get; set; }
    /// <summary>Mean F0 over the phrase's voiced frames.</summary>
    public double? MeanHz { get; set; }
    /// <summary>Semitone SD over the phrase's in-register frames ("true melody").</summary>
    public double? MelodySt { get; set; }
    /// <summary>Mean F0 over the last 0.12 s (same as <see cref="Phrase.OffsetHz"/>).</summary>
    public double? OffsetHz { get; set; }
    /// <summary>Median F2 of the gated vowel-core frames inside the phrase.</summary>
    public double? F2Hz { get; set; }
    /// <summary>Mean corrected H1*–A3* of the weight frames inside the phrase.</summary>
    public double? WeightDb { get; set; }
}

public sealed class Frames
{
    public List<double> T { get; set; } = [];
    /// <summary>Null where the frame is unvoiced.</summary>
    public List<double?> Hz { get; set; } = [];
}

public sealed class Phrase
{
    public double Start { get; set; }
    public double End { get; set; }
    public double OnsetHz { get; set; }
    public double OffsetHz { get; set; }
    public double MinHz { get; set; }
    public bool StartedInRegister { get; set; }
    public bool EndedInRegister { get; set; }
    public double SubRegisterPct { get; set; }
}
