using Reyfen.Timbratune.Core.Analysis.Metrics;

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
/// </summary>
public sealed class TrendSlice(int metricCount)
{
    public double T { get; set; }
    public double Start { get; set; }
    public double End { get; set; }

    /// <summary>
    /// Each metric's value in the slice, by <see cref="MetricId.Index"/> of the registry that made
    /// it (the metrics with a trend column fill theirs); null = not measurable in that slice.
    /// Saved under today's names (<c>Json/MetricColumnsJson</c>).
    /// </summary>
    public double?[] Values { get; } = new double?[metricCount];

    public double? this[MetricId id] => Values[id.Index];
}

/// <summary>One phrase's metrics (the metrics with a phrase column), by <see cref="MetricId.Index"/>.</summary>
public sealed class PhraseMetrics(int metricCount)
{
    public double Start { get; set; }
    public double End { get; set; }

    /// <summary>Each metric's value in the phrase, by <see cref="MetricId.Index"/>; null = not measurable.</summary>
    public double?[] Values { get; } = new double?[metricCount];

    public double? this[MetricId id] => Values[id.Index];
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
