using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Metrics.Clarity;
using Reyfen.Timbratune.Core.Analysis.Metrics.Loudness;
using Reyfen.Timbratune.Core.Analysis.Metrics.Phrasing;
using Reyfen.Timbratune.Core.Analysis.Metrics.Pitch;
using Reyfen.Timbratune.Core.Analysis.Metrics.Resonance;
using Reyfen.Timbratune.Core.Analysis.Metrics.Steadiness;
using Reyfen.Timbratune.Core.Analysis.Metrics.Weight;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>The trend and phrase columns' ids in the app's registry.</summary>
internal static class Col
{
    private static MetricId Of(MetricInfo metric) => MetricRegistry.Default.Id(metric);

    public static readonly MetricId Pitch = Of(PitchModule.Mean), PitchSd = Of(PitchModule.Variability), Melody = Of(PhrasingModule.Melody),
        Endings = Of(PhrasingModule.Endings), Loudness = Of(LoudnessModule.Loudness), F2 = Of(ResonanceModule.F2), F3 = Of(ResonanceModule.F3),
        Weight = Of(WeightModule.Weight), Hnr = Of(ClarityModule.Hnr), Jitter = Of(SteadinessModule.Jitter);
}
