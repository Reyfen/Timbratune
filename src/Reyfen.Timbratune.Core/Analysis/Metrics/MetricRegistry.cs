using Reyfen.Timbratune.Core.Analysis.Tracks;

namespace Reyfen.Timbratune.Core.Analysis.Metrics;

/// <summary>
/// The metric modules, in order. The analysis runs every registered module; the order is the
/// single source of display order once the UI reads the registry (P7). Saved series keep the
/// file's own key order whatever this order is (see <see cref="FrameSeriesBuilder.ForTake"/>).
/// </summary>
public sealed class MetricRegistry(IReadOnlyList<IMetricModule> modules)
{
    /// <summary>The app's modules.</summary>
    public static MetricRegistry Default { get; } = new([new Pitch.PitchModule(), new Resonance.ResonanceModule(), new Weight.WeightModule(), new Loudness.LoudnessModule(), new Clarity.ClarityModule(), new Steadiness.SteadinessModule(), new Phrasing.PhrasingModule()]);

    public IReadOnlyList<IMetricModule> Modules { get; } = modules;

    /// <summary>Every track some module reads, with their dependencies.</summary>
    public TrackSet RequiredTracks => Modules.Aggregate(TrackSet.None, (set, m) => set | m.RequiredTracks).WithDependencies();

    /// <summary>The index of the module of type <typeparamref name="T"/>, or −1.</summary>
    public int IndexOf<T>() where T : IMetricModule
    {
        for (var i = 0; i < Modules.Count; i++)
            if (Modules[i] is T) return i;
        return -1;
    }
}
