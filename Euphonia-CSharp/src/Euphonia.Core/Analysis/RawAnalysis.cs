namespace Euphonia.Core.Analysis;

/// <summary>
/// Raw measurements of one take, before any statistics: what
/// <see cref="AcousticsAnalysisEngine"/> measures (the steps of analyze.py's
/// Praat calls) and <see cref="AnalysisPostProcessor"/> turns into metrics.
/// </summary>
public sealed class RawAnalysis
{
    /// <summary>
    /// Scalars: duration, sampling_frequency, pitch_mean/median/min/max/sd,
    /// hnr, jitter_local, shimmer_local, intensity_mean/min/max (NaN = undefined).
    /// </summary>
    public required IReadOnlyDictionary<string, double> Summary { get; init; }
    /// <summary>Vowel-core candidates that passed the F1 gate, in time order per ceiling.</summary>
    public required IReadOnlyList<FormantRow> Formants { get; init; }
    public required IReadOnlyList<WeightRow> WeightFrames { get; init; }
    public required IReadOnlyList<(double X, double Y)> Ltas { get; init; }
    /// <summary>10 ms pitch track; Hz &lt;= 0 means unvoiced.</summary>
    public required IReadOnlyList<(double T, double Hz)> Contour { get; init; }
    public required IReadOnlyList<(double Start, double End)> Sounding { get; init; }

    public double Get(string key) => Summary.TryGetValue(key, out var v) ? v : double.NaN;

    public readonly record struct FormantRow(double Ceiling, double T, double F1, double F2, double F3);

    public readonly record struct WeightRow(
        double T, double F0, double F1, double F2, double F3,
        double B1, double B2, double B3,
        double H1, double A3, double K3);
}
