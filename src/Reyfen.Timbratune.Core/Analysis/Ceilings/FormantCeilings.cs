namespace Reyfen.Timbratune.Core.Analysis.Ceilings;

/// <summary>How the formant ceiling (the highest formant frequency Burg's analysis looks for) is chosen.</summary>
public enum FormantCeilingMethod
{
    /// <summary>
    /// The ceiling follows the voice: five candidate ceilings, and per moment the one whose tracks
    /// are smoothest (<see cref="CeilingPathFinder"/>), so one take can hold several voices.
    /// </summary>
    Path,

    /// <summary>
    /// analyze.py's choice (v0.3.0 and older): 5500 and 5000 Hz, the one with the steadier F2 over the
    /// take, frames centred in the sound. Kept for the parity tests and takes analysed with it.
    /// </summary>
    Legacy,
}

/// <summary>The candidate ceilings of each method.</summary>
public static class FormantCeilings
{
    /// <summary>The candidates of <see cref="FormantCeilingMethod.Path"/>, lowest first (RESEARCH-ALGORITHMS §2.4, §12).</summary>
    public static IReadOnlyList<double> PathCandidates { get; } = [4500, 5000, 5500, 6000, 6500];

    /// <summary>The ceilings of <see cref="FormantCeilingMethod.Legacy"/>, in analyze.py's order.</summary>
    public static IReadOnlyList<double> LegacyCandidates { get; } = [RawAnalysisAssembler.FormantCeiling, 5000];

    /// <summary>Where nothing can be scored (no voiced piece), the path uses 5500 Hz throughout.</summary>
    public const double FallbackHz = RawAnalysisAssembler.FormantCeiling;

    public static IReadOnlyList<double> Candidates(FormantCeilingMethod method) =>
        method == FormantCeilingMethod.Legacy ? LegacyCandidates : PathCandidates;

    /// <summary>
    /// The frame grid's origin: the path's frames are on multiples of the frame step from the start of
    /// the recording, so live and saved frames coincide; Legacy keeps the centred frames (null).
    /// </summary>
    public static double? GridOrigin(FormantCeilingMethod method) => method == FormantCeilingMethod.Legacy ? null : 0;

    /// <summary>How a saved take's formants were measured (takes from before v0.4.0: Legacy).</summary>
    public static FormantCeilingMethod MethodOf(Models.Recording take) =>
        take.Formants.Method == Models.Formants.PathMethod ? FormantCeilingMethod.Path : FormantCeilingMethod.Legacy;

    /// <summary>The index of <see cref="FallbackHz"/> among <paramref name="candidates"/> (0 if absent).</summary>
    public static int FallbackIndex(IReadOnlyList<double> candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
            if (candidates[i] == FallbackHz) return i;
        return 0;
    }
}
