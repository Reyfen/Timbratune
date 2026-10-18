namespace Reyfen.Timbratune.Core.Analysis.Ceilings;

/// <summary>What a candidate ceiling's formants are compared with in <see cref="CeilingPathFinder.Instability"/>.</summary>
public enum StabilityReference
{
    /// <summary>The nearer of the two neighbouring ceilings (the one it differs from least): a plateau of two.</summary>
    NearerNeighbour,

    /// <summary>The mean distance to both neighbours (one at the ends): a plateau of three.</summary>
    BothNeighbours,

    /// <summary>The median distance to all the other candidates: the ceiling most of the others agree with (favours the middle ceiling).</summary>
    AllOthersMedian,
}

/// <summary>
/// How <see cref="CeilingPathFinder"/> scores a voiced piece on a candidate ceiling (lower is better):
/// <see cref="SmoothnessWeight"/> × log W (Weenink 2015's smoothness, <see cref="Acoustics.Formants.FormantSmoothness"/>)
/// plus <see cref="StabilityWeight"/> × log(instability + <see cref="StabilityFloor"/>), the instability being how far the
/// candidate's formants sit from the neighbouring candidates' (<see cref="CeilingPathFinder.Instability"/>).
/// <para>
/// Smoothness alone (v0.4.0) can't tell the ceilings apart on a high-pitched, soft voice: the poles sit on the
/// sparse harmonics at every ceiling, and a track that follows harmonics is smooth even when a too-low ceiling
/// has slipped the labels (HIGH-PITCH-PLAN.md, HANDOFF §8v). The stability term is the ceiling sweep of
/// manual practice: the right ceiling is on the plateau where the formants stop moving with the ceiling. It
/// reads only the formant tracks, never the pitch, so pitch and size stay separate measurements.
/// </para>
/// </summary>
/// <param name="SmoothnessWeight">Weight of log W; 1 = v0.4.0's score.</param>
/// <param name="StabilityWeight">Weight of the stability term; 0 = off (v0.4.0).</param>
/// <param name="StabilityFloor">Added to the instability before the log (in its unit: Hz, or ‰ when <paramref name="Relative"/>), so a perfectly stable piece has a finite score.</param>
/// <param name="Reference">Which other candidates a candidate's formants are compared with.</param>
/// <param name="FirstFormant">The lowest formant compared (2 = F2).</param>
/// <param name="LastFormant">The highest formant compared (3 = F3, 4 = F4).</param>
/// <param name="Relative">Compare |ln(f / f′)| × 1000 (‰; 1 ‰ is about 1.7 % of a semitone) instead of Hz, so F4 doesn't outweigh F2.</param>
/// <param name="MedianStabilityWeight">Weight of a second stability term against the median of all the other candidates
/// (<see cref="StabilityReference.AllOthersMedian"/>), added to the one of <paramref name="Reference"/>; 0 = off.</param>
/// <param name="ContinuityWeight">
/// The cost of leaving a reading between consecutive pieces when it could have been continued: per ‰ by which the
/// mean relative change of <paramref name="FirstFormant"/>–<paramref name="ContinuityLastFormant"/> from one piece's levels (the mean
/// of the middle half of its frames, on the ceiling chosen there) to the next piece's (on the ceiling chosen there) exceeds the smallest change any candidate
/// offers on the next piece (each formant's change capped at <see cref="ContinuityCap"/> ‰); 0 = off. Consecutive
/// pieces overlap by half, so a vowel moving every candidate's formants costs nothing, while a slip between two
/// readings costs about <see cref="ContinuityCap"/> × this whether the ceiling changed there or not (HANDOFF §8x: a
/// voice with six resonances below 6 kHz has two self-consistent readings, and without this the path hops between them).
/// </param>
/// <param name="ContinuityLastFormant">The highest formant the continuity term reads (4 = F4, where the two readings of a voice
/// like take 24's differ most; on F2–F3 alone the term didn't hold the reading, HANDOFF §8x). Across a pause only F3 up
/// to this count: F2 belongs to the vowel, and the next phrase starts on another one.</param>
/// <param name="ContinuityDeadZone">An excess (‰) under this costs nothing: continuations that agree this closely are
/// a tie, decided by the piece costs alone, so a live block's last frames can't move a switch between agreeing ceilings.</param>
/// <param name="ContinuityPauseLastFormant">The highest formant the continuity term reads across a pause (from F3 up).</param>
/// <param name="BandwidthFloorHz">Every bandwidth counts as at least this in Weenink's W (<see cref="Acoustics.Formants.FormantSmoothness.Criterion(Acoustics.Formants.IFormantFrames, int, int, double)"/>); 0 = off.</param>
/// <param name="ContinuityWithinStretches">Also charge a change of ceiling inside a voiced stretch by how far the two
/// ceilings' F2–F4 sit apart on the frames the two pieces share. Off by default: on the same frames a real change of voice
/// looks like a label jump too, so the term delays following one, and a delayed change is a near tie the live frames can
/// tip (HANDOFF §8x: the man + woman splice); the hopping it was meant to stop happened at pauses anyway.</param>
public sealed record CeilingScoring(double SmoothnessWeight = 1, double StabilityWeight = 0, double StabilityFloor = 10,
    StabilityReference Reference = StabilityReference.NearerNeighbour, int FirstFormant = 2, int LastFormant = 3, bool Relative = false,
    double MedianStabilityWeight = 0, double ContinuityWeight = 0, int ContinuityLastFormant = 4, double ContinuityDeadZone = 0,
    int ContinuityPauseLastFormant = 4, bool ContinuityWithinStretches = false, double BandwidthFloorHz = 0)
{
    /// <summary>The largest change of one formant between pieces that <see cref="ContinuityWeight"/> charges for (‰; a label slip is 300–600).</summary>
    public const double ContinuityCap = 600;


    /// <summary>v0.4.0's scoring: smoothness only.</summary>
    public static CeilingScoring Smoothness { get; } = new();

    /// <summary>
    /// The app's scoring since HIGH-PITCH-PLAN (HANDOFF §8w): smoothness plus 3 × the log of the relative distance
    /// (‰, floor 30) of F2–F4 from the nearer neighbouring ceiling plus 2 × the same from the median of all the
    /// others. The nearer neighbour finds the plateau where the formants stop moving with the ceiling; the median
    /// keeps the choice from resting on the one small distance between two neighbours. Measured against v0.4.0's
    /// smoothness (both with the anchored resampling): the owner's takes 13.5 / 14.4 cm (Praat at 5500 Hz 13.6 / 14.4;
    /// v0.4.0 13.6 / 18.1); EARS women reading high, beyond +1 cm bigger 30 % → 7 % (p90 +4.4 → +0.9 cm); men 5 % → 2 %;
    /// take-to-take ICC 0.93 → 0.94; VTR F3 RMSE women 217 → 202 Hz, men 176 → 174 Hz; trans/masc r 0.77 → 0.79.
    /// </summary>
    public static CeilingScoring Plateau { get; } = new(SmoothnessWeight: 1, StabilityWeight: 3, StabilityFloor: 30,
        Reference: StabilityReference.NearerNeighbour, FirstFormant: 2, LastFormant: 4, Relative: true, MedianStabilityWeight: 2);

    /// <summary>
    /// <see cref="Plateau"/> plus the continuity of the chosen reading between pieces (<see cref="ContinuityWeight"/>):
    /// the owner's low voice (HANDOFF §8x) has two self-consistent readings and the plateau scoring alone hops between
    /// them every second or so; this holds one reading per stretch and decides it on the whole stretch's evidence.
    /// </summary>
    public static CeilingScoring Steady { get; } = Plateau with { ContinuityWeight = 0.12, ContinuityDeadZone = 50 };

    /// <summary>
    /// What a finder made without a scoring uses: the providers' finders read it when they are made. The app's is
    /// <see cref="Steady"/>; dev tools (formant-check -stability …) set it for experiments.
    /// </summary>
    public static CeilingScoring Default { get; set; } = Steady;
}
