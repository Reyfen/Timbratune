using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;

namespace Reyfen.Timbratune.Core.Analysis.Tracks;

/// <summary>
/// The tracks of one update, saved or live: one typed field per track, no lookups. A field is
/// only meaningful when its track is in <see cref="Tracks"/>. Valid for the update that made it
/// (live, the derived lists are refilled by the next one).
/// </summary>
public readonly struct TrackView
{
    public required TrackSet Tracks { get; init; }
    public required double Duration { get; init; }
    public required double SamplingFrequency { get; init; }
    /// <summary>The samples the tracks were measured on (weight rows and shimmer read them).</summary>
    public required Sound Sound { get; init; }

    public PitchContour Pitch { get; init; }
    public IntensityContour Intensity { get; init; }
    public HarmonicityContour Harmonicity { get; init; }
    public IFormantTrack Formants5500 { get; init; }
    public IFormantTrack Formants5000 { get; init; }
    /// <summary>LTAS samples at 100, 200, … 5000 Hz; empty when not measured.</summary>
    public IReadOnlyList<(double X, double Y)> Ltas { get; init; }
    /// <summary>Glottal pulse times, sorted.</summary>
    public IReadOnlyList<double> Pulses { get; init; }
    /// <summary>Per voiced stretch: its end and local jitter (fraction; NaN = too few pulses).</summary>
    public IReadOnlyList<(double End, double Jitter)> StretchJitter { get; init; }
    /// <summary>The weight row of a voiced frame (time, F0), or null; thread-safe, memoised live.</summary>
    public Func<double, double, RawAnalysis.WeightRow?> WeightRow { get; init; }
    /// <summary>Voiced frames, sounding stretches, the loud-vowel gate and the derived tracks.</summary>
    public DerivedTracks Derived { get; init; }
}

/// <summary>
/// The tracks computed from other tracks (stages 2 and 3): voiced frames, sounding stretches,
/// the loud-vowel gate, and the derived vowel formant rows and weight frames. The live
/// provider keeps one and refills it; the voiced frames and the gate are reused lists.
/// </summary>
public sealed class DerivedTracks
{
    /// <summary>The gate's outlier share (see <see cref="RawAnalysisAssembler.LoudOutlierFraction"/>).</summary>
    public double LoudOutlierFraction { get; internal set; } = RawAnalysisAssembler.LoudOutlierFraction;

    /// <summary>What has been computed into this object by the current update.</summary>
    public TrackSet Tracks { get; internal set; }

    public List<(double T, double F0)> Voiced { get; } = [];
    public IReadOnlyList<(double Start, double End)> Sounding { get; internal set; } = [];
    /// <summary>The loud-vowel gate: times of voiced frames within 10 dB of the loudness reference.</summary>
    public List<double> LoudTimes { get; } = [];
    internal List<double> LoudScratch { get; } = [];

    /// <summary>The gated times with a plausible F1 at each ceiling (a new list per update).</summary>
    public IReadOnlyList<RawAnalysis.FormantRow> VowelFormantRows { get; internal set; } = [];
    /// <summary>How many gated times were tried for <see cref="VowelFormantRows"/> (≤ 300).</summary>
    public int FormantFramesTried { get; internal set; }
    /// <summary>Weight rows of ≤ 250 voiced frames (a new list per update).</summary>
    public IReadOnlyList<RawAnalysis.WeightRow> WeightFrames { get; internal set; } = [];

    /// <summary>Whether the gate (and what follows from it) was made with this outlier share.</summary>
    internal bool GateFor(double outlierFraction) =>
        Tracks.Has(TrackSet.LoudVowelGate) && LoudOutlierFraction.Equals(outlierFraction);

    /// <summary>Stage 2 (after pitch and intensity): voiced frames, sounding stretches, the gate.</summary>
    internal void ComputeStage2(PitchContour pitch, IntensityContour intensity, TrackSet set, double outlierFraction)
    {
        LoudOutlierFraction = outlierFraction;
        Tracks = TrackSet.None;
        if (set.Has(TrackSet.VoicedFrames))
        {
            RawAnalysisAssembler.VoicedFrames(pitch, Voiced);
            Tracks |= TrackSet.VoicedFrames;
        }
        if (set.Has(TrackSet.Sounding))
        {
            Sounding = RawAnalysisAssembler.SoundingIntervals(intensity);
            Tracks |= TrackSet.Sounding;
        }
        if (set.Has(TrackSet.LoudVowelGate))
        {
            RawAnalysisAssembler.LoudVoicedTimes(Voiced, intensity, LoudTimes, outlierFraction, LoudScratch);
            Tracks |= TrackSet.LoudVowelGate;
        }
    }

    /// <summary>Stage 3: the derived tracks, from the gate, the voiced frames and the formants.</summary>
    internal void ComputeStage3(IFormantTrack formants5500, IFormantTrack formants5000,
        Func<double, double, RawAnalysis.WeightRow?> weightRow, TrackSet set)
    {
        if (set.Has(TrackSet.VowelFormantRows))
        {
            // vowel_formants(): the gated times subsampled to ≤ 300 (Subsample returns its input when
            // it is short enough: then a copy, as the gate list is reused).
            var selected = LoudTimes.Count <= 300 ? new List<double>(LoudTimes) : RawAnalysisAssembler.Subsample(LoudTimes, 300);
            VowelFormantRows = RawAnalysisAssembler.VowelCoreFormants(selected,
                [(RawAnalysisAssembler.FormantCeiling, formants5500), (5000, formants5000)]);
            FormantFramesTried = selected.Count;
            Tracks |= TrackSet.VowelFormantRows;
        }
        if (set.Has(TrackSet.WeightFrames))
        {
            WeightFrames = RawAnalysisAssembler.WeightFrames(Voiced, weightRow);
            Tracks |= TrackSet.WeightFrames;
        }
    }
}
