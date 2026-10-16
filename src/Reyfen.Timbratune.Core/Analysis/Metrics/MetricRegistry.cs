using Reyfen.Timbratune.Core.Analysis.Tracks;

namespace Reyfen.Timbratune.Core.Analysis.Metrics;

/// <summary>A metric's identity in one registry: its index (columns, live series) and stable name.</summary>
public readonly record struct MetricId(int Index, string Name)
{
    public override string ToString() => $"{Name}#{Index}";
}

/// <summary>A metric as registered: its id, its description and the module that measures it.</summary>
public sealed record RegisteredMetric(MetricId Id, MetricInfo Info, IMetricModule Module);

/// <summary>
/// The metric modules, in order, and their metrics, numbered in the same order. The analysis runs
/// every registered module; the UI shows the metrics in this order (cards, trends, live timelines,
/// chips). Saved files keep their own key order whatever this order is (see
/// <see cref="FrameSeriesBuilder.ForTake(in TrackView)"/> and <c>Json/MetricColumnsJson</c>).
/// </summary>
public sealed class MetricRegistry
{
    private readonly int[] _offsets;

    public MetricRegistry(IReadOnlyList<IMetricModule> modules)
    {
        Modules = modules;
        _offsets = new int[modules.Count];
        var metrics = new List<RegisteredMetric>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var m = 0; m < modules.Count; m++)
        {
            _offsets[m] = metrics.Count;
            var own = modules[m].Metrics;
            for (var k = 0; k < own.Count; k++)
            {
                var info = own[k];
                if (info.Slot != k) throw new InvalidOperationException($"{modules[m].Name}: number its metrics with MetricInfo.Numbered.");
                if (!names.Add(info.Name)) throw new InvalidOperationException($"Two metrics are called '{info.Name}'.");
                metrics.Add(new RegisteredMetric(new MetricId(metrics.Count, info.Name), info, modules[m]));
            }
        }
        Metrics = metrics;
    }

    /// <summary>The app's modules: pitch and phrasing first, then loudness, the resonance and weight cues, and the skill cues.</summary>
    public static MetricRegistry Default { get; } = new([new Pitch.PitchModule(), new Phrasing.PhrasingModule(), new Loudness.LoudnessModule(),
        new Resonance.ResonanceModule(), new Weight.WeightModule(), new Clarity.ClarityModule(), new Steadiness.SteadinessModule()]);

    public IReadOnlyList<IMetricModule> Modules { get; }

    /// <summary>Every module's metrics, in order; <see cref="MetricId.Index"/> is the position here.</summary>
    public IReadOnlyList<RegisteredMetric> Metrics { get; }

    public int Count => Metrics.Count;

    public MetricInfo this[MetricId id] => Metrics[id.Index].Info;

    /// <summary>Every track some module reads, with their dependencies.</summary>
    public TrackSet RequiredTracks => Modules.Aggregate(TrackSet.None, (set, m) => set | m.RequiredTracks).WithDependencies();

    /// <summary>The id of a registered metric.</summary>
    public MetricId Id(MetricInfo metric) =>
        TryGetId(metric, out var id) ? id : throw new KeyNotFoundException($"Metric '{metric.Name}' isn't registered.");

    public bool TryGetId(MetricInfo metric, out MetricId id)
    {
        foreach (var m in Metrics)
            if (ReferenceEquals(m.Info, metric))
            {
                id = m.Id;
                return true;
            }
        id = default;
        return false;
    }

    /// <summary>The metric with this <see cref="MetricInfo.Name"/>, or null.</summary>
    public MetricId? Find(string name)
    {
        foreach (var m in Metrics)
            if (m.Info.Name == name) return m.Id;
        return null;
    }

    /// <summary>The index of the module of type <typeparamref name="T"/>, or −1.</summary>
    public int IndexOf<T>() where T : IMetricModule
    {
        for (var i = 0; i < Modules.Count; i++)
            if (Modules[i] is T) return i;
        return -1;
    }

    /// <summary>Module <paramref name="module"/>'s view of a column array (indexed by <see cref="MetricId.Index"/>).</summary>
    public MetricValues ValuesOf(int module, double?[] values) => new(values, _offsets[module]);

    /// <summary>Module <paramref name="module"/>'s view of the series buffers.</summary>
    public SeriesLines LinesOf(int module, FrameSeriesBuffers buffers) => new(buffers, _offsets[module]);
}

/// <summary>A module's columns in an array by metric index: <c>values[metric] = …</c> with its own metrics.</summary>
public readonly struct MetricValues(double?[] values, int offset)
{
    public double? this[MetricInfo metric]
    {
        get => values[offset + metric.Slot];
        set => values[offset + metric.Slot] = value;
    }
}

/// <summary>A module's series lists in the pooled buffers, by its own metrics (no allocation).</summary>
public readonly struct SeriesLines(FrameSeriesBuffers buffers, int offset)
{
    /// <summary>The shared buffers (scratch lists; whether the series are for a saved take).</summary>
    public FrameSeriesBuffers Buffers => buffers;

    public List<TimedValue> this[MetricInfo metric] => buffers.Lines[offset + metric.Slot];
}
