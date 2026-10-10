namespace Reyfen.Timbratune.Core.Models;

// Mirrors analysis/<id>.series.json: the take's per-frame lists, kept apart from
// analysis/<id>.json so the dashboard loads as fast as before. Only read for export.

/// <summary>
/// The per-frame series of a saved take, as the live graphs define them (see
/// <c>FrameSeriesBuilder</c>), measured by the full analysis of the saved WAV.
/// </summary>
public sealed class TakeSeries
{
    /// <summary>What these series hold; bump when a series is added or redefined.</summary>
    public const int CurrentVersion = 1;

    public const string Pitch = "pitch", Loudness = "loudness", Hnr = "hnr", F1 = "f1", F2 = "f2", F3 = "f3",
        Weight = "weight", Jitter = "jitter";

    public int Version { get; set; } = CurrentVersion;

    /// <summary>By name: pitch, loudness, hnr, f1, f2, f3, weight, jitter.</summary>
    public Dictionary<string, FrameSeries> Series { get; set; } = [];
}

/// <summary>One series: parallel lists of times and values.</summary>
public sealed class FrameSeries
{
    /// <summary>Unit of <see cref="Values"/>: "Hz", "dB" or "%".</summary>
    public string Unit { get; set; } = "";

    /// <summary>
    /// The analysis frame spacing (s) the times sit on; points may skip frames (e.g. only
    /// voiced ones). Null when the points have no regular spacing (jitter per voiced stretch).
    /// </summary>
    public double? StepS { get; set; }

    /// <summary>What a point stands for, e.g. "voiced 10 ms frame".</summary>
    public string Description { get; set; } = "";

    /// <summary>Times (s from the start of the take).</summary>
    public List<double> T { get; set; } = [];

    /// <summary>Values at <see cref="T"/>; null = undefined there (e.g. unvoiced pitch).</summary>
    public List<double?> Values { get; set; } = [];
}
