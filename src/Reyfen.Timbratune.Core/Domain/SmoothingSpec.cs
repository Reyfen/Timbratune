namespace Reyfen.Timbratune.Core.Domain;

/// <summary>How the values inside a smoothing window are combined.</summary>
public enum WindowStatistic
{
    /// <summary>Robust to the odd wrong frame (formant jumps, HNR spikes).</summary>
    Median,
    /// <summary>Mean of 10^(dB/10), back in dB — how loudness is averaged.</summary>
    EnergyMean,
    /// <summary>Sample standard deviation — how much the value moves.</summary>
    StandardDeviation,
}

/// <summary>
/// Smoothed lines for the live timelines. Each value is the statistic over a window
/// <em>centred</em> on its time, evaluated on a fixed time grid. Near the live end the
/// window only has the past, so the newest values are a quick provisional estimate;
/// as more audio arrives the window fills in and those values are corrected in place.
/// Lines break where the data has a gap (a <see cref="double.NaN"/> value), and a
/// window never reaches across one.
/// <para>
/// Layers, most to least certain: the saved full analysis (after Stop); the live frame
/// analysis (the points); a window with enough data on both sides ("settled"); a window
/// missing part of its future (the newest ~half window); and a prediction. The unsettled
/// tail is blended toward a damped trend of the settled line (<see cref="TrendDamping"/>
/// of its recent slope) — the less future its window has, the more it leans on that
/// prediction — and the ongoing stretch is extended to "now" by the same prediction, so
/// the current point is always shown and moves steadily, still leaning the right way.
/// </para>
/// </summary>
public sealed record SmoothingSpec(
    WindowStatistic Statistic,
    double HalfWidth,
    int MinimumCount,
    double MaxGap,
    double Step = 0.05)
{
    /// <summary>Radius (grid steps) of the light mean applied after the window statistic.</summary>
    public int PolishRadius { get; init; } = 2;

    /// <summary>Fraction of the settled line's recent slope the prediction follows (0 = flat, 1 = full trend).</summary>
    public double TrendDamping { get; init; } = 0.5;

    /// <summary>Weight kept by an unsettled window's own value at the very end (the rest is the prediction).</summary>
    public double MinimumTrust { get; init; } = 0.15;

    /// <summary>How much settled line (s) the trend's slope is fitted over.</summary>
    public double TrendSeconds { get; init; } = 1.0;

    /// <summary>How much of its future half-width a window needs to count as settled (1 = all of it).</summary>
    public double SettledCoverage { get; init; } = 0.5;

    /// <summary>
    /// When set, the line breaks where one grid step jumps by more than this ratio (e.g. an
    /// octave slip in pitch), instead of drawing a near-vertical connection. Positive values only.
    /// </summary>
    public double? MaxStepRatio { get; init; }

    /// <summary>
    /// When set, stretches separated by a gap up to this long (s) are joined by a straight
    /// line instead of a break — the gap is drawn, not smoothed over (no window reaches
    /// across it). Breaks given to the smoother (RecentWindow.Smooth in the UI) are never bridged.
    /// </summary>
    public double? BridgeGap { get; init; }
}
