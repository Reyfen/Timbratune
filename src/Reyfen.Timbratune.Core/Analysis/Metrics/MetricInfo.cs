using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis.Metrics;

/// <summary>
/// One quantity a metric module measures and shows (REFACTOR-PLAN §2.4): its name, unit and
/// zones, its saved columns, and how each surface presents it: a stat card, the comparison
/// across takes, a trend chart, a live timeline, a chip on the take's card. A surface it has no
/// presentation for doesn't show it. The UI builds every surface by iterating
/// <see cref="MetricRegistry"/>, so the registry's order is the display order.
/// </summary>
public sealed class MetricInfo
{
    /// <summary>Stable name (analytics, settings; the saved column's key when it has no <see cref="ColumnKey"/>).</summary>
    public required string Name { get; init; }

    public required string Title { get; init; }

    /// <summary>"Hz", "dB", "%", "st".</summary>
    public required string Unit { get; init; }

    /// <summary>Its zones (cards, charts, live dot colour); null = no zones.</summary>
    public IReadOnlyList<Zone>? Zones { get; init; }

    /// <summary>The usual span of the scale (zone bar, comparison, live axis).</summary>
    public double Lo { get; init; }
    public double Hi { get; init; }

    /// <summary>The take's value (cards, chips, comparison); null = the take doesn't hold one.</summary>
    public Func<Recording, double?>? Take { get; init; }

    /// <summary>Which saved per-slice / per-phrase columns it has.</summary>
    public MetricColumns Columns { get; init; }

    /// <summary>The saved columns' key in detail.json (today's v2 names); null = <see cref="Name"/>.</summary>
    public string? ColumnKey { get; init; }

    public string Key => ColumnKey ?? Name;

    public StatCard? Card { get; init; }
    public Comparison? Compare { get; init; }
    public TrendChart? Trend { get; init; }
    public LiveTimeline? Live { get; init; }
    public Chip? Chip { get; init; }

    /// <summary>Its position in its module's <see cref="IMetricModule.Metrics"/> (set by <see cref="Numbered"/>).</summary>
    public int Slot { get; private set; } = -1;

    /// <summary>A module's metrics, numbered in order (each metric belongs to one module).</summary>
    public static IReadOnlyList<MetricInfo> Numbered(params MetricInfo[] metrics)
    {
        for (var i = 0; i < metrics.Length; i++)
        {
            if (metrics[i].Slot >= 0 && metrics[i].Slot != i) throw new InvalidOperationException($"Metric '{metrics[i].Name}' is already numbered elsewhere.");
            metrics[i].Slot = i;
        }
        return metrics;
    }

    public override string ToString() => Name;
}

[Flags]
public enum MetricColumns
{
    None = 0,
    /// <summary>A column of the time trends (and a trend chart's points).</summary>
    Trend = 1,
    /// <summary>A column of the per-phrase breakdown.</summary>
    Phrase = 2,
}

/// <summary>A stat card: the take's value with its zone bar, and a line under it (markup).</summary>
public sealed record StatCard(Func<Recording, string> Sub);

/// <summary>The comparison across takes and reference voices (tapping a card or gauge).</summary>
/// <param name="Gendered">Real men's and women's reference ticks are meaningful.</param>
/// <param name="Blurb">One line of framing in the comparison's header.</param>
public sealed record Comparison(Func<ReferenceVoice, double?> Reference, bool Gendered, string Blurb);

/// <summary>A "trends within this take" chart of the metric's trend column.</summary>
/// <param name="ColorKey">The theme colour of its line ("Chart1" …).</param>
public sealed record TrendChart(string Caption, string ColorKey)
{
    /// <summary>The caption for a register floor (Hz), when it mentions it.</summary>
    public Func<double, string>? FloorCaption { get; init; }

    /// <summary>Background zones for a register floor (Hz); null = the metric's zones.</summary>
    public Func<double, IReadOnlyList<Zone>>? FloorBands { get; init; }

    /// <summary>One highlighted band instead of zones.</summary>
    public HighlightBand? Band { get; init; }

    /// <summary>
    /// Trends saved before this version (0 = rebuilt from the contour) lack the column: the chart says
    /// "measuring…" while the take is re-analyzed. Null = always there (rebuilt from the contour).
    /// </summary>
    public int? MeasuredSinceVersion { get; init; }
}

/// <summary>A band from <paramref name="From"/> to <paramref name="To"/> in a theme colour ("ZoneFem").</summary>
public sealed record HighlightBand(double From, double To, string ColorKey);

/// <summary>A live timeline while recording: the metric's points smoothed over a window.</summary>
public sealed record LiveTimeline(string Caption, SmoothingSpec Smoothing)
{
    /// <summary>Keep showing the last value however old it is (loudness: always measured, silence included).</summary>
    public bool HoldValue { get; init; }

    /// <summary>Its points when they aren't its module's live series (e.g. made from the pitch contour).</summary>
    public Func<LiveLineInputs, IReadOnlyList<TimedValue>>? Points { get; init; }
}

/// <summary>A chip on the take's card: its value and a short label under it ("pitch Hz").</summary>
public sealed record Chip(string Label)
{
    /// <summary>The chip's text; null = the take's value.</summary>
    public Func<Recording, string>? Text { get; init; }
}

/// <summary>What live lines made outside the analysis are built from (once per refresh).</summary>
public sealed class LiveLineInputs(RecordingDetail? detail, IReadOnlyList<TimedValue> voicedPitch, double registerFloorHz)
{
    /// <summary>The pitch frames and phrases so far.</summary>
    public RecordingDetail? Detail { get; } = detail;

    /// <summary>The voiced pitch frames (Hz).</summary>
    public IReadOnlyList<TimedValue> VoicedPitch { get; } = voicedPitch;

    public double RegisterFloorHz { get; } = registerFloorHz;
}

/// <summary>Shared live smoothing settings (measured with tools/live-steadiness, HANDOFF §6).</summary>
public static class LiveSmoothing
{
    /// <summary>Per-frame values (HNR): median over about 1 s.</summary>
    public static readonly SmoothingSpec Frame = new(WindowStatistic.Median, 0.5, 5, 0.4);

    /// <summary>Values on loud vowels (F2, F3, weight): median over about 2 s.</summary>
    public static readonly SmoothingSpec Vowel = new(WindowStatistic.Median, 1.0, 6, 0.8) { PolishRadius = 4 };

    /// <summary>How much a value moves (pitch variability, melody): SD over about 2 s.</summary>
    public static readonly SmoothingSpec Movement = new(WindowStatistic.StandardDeviation, 1.0, 10, 0.4);
}
