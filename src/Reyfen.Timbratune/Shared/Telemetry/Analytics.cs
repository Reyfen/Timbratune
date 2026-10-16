using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Shared.Telemetry;

/// <summary>
/// Usage analytics (REFACTOR-PLAN §5): one event per finished take, never live data. Off unless
/// the user opted in and this install is in the rollout (<see cref="TelemetryConsent"/>); the
/// default is <see cref="NullAnalytics"/>. Call sites check <see cref="IsEnabled"/> first, so a
/// disabled build creates no event at all:
/// <code>if (_analytics.IsEnabled) _analytics.TakeAnalyzed(new(...));</code>
/// The adapter (Mixpanel, later) sends in the background and drops what fails.
/// </summary>
public interface IAnalytics
{
    bool IsEnabled { get; }

    void TakeAnalyzed(in TakeAnalyzedEvent e);
}

public enum TakeSource { Recorded, Imported }

/// <summary>
/// A take finished its analysis. The adapter sends only what <see cref="AnalyticsPayload"/>
/// derives from it (zones, rounded durations), never the label, note, file names or raw values.
/// </summary>
/// <param name="ProcessingSeconds">Stop to saved for a recording; the whole import (decoding included) for an import.</param>
public readonly record struct TakeAnalyzedEvent(TakeSource Source, Recording Take, double ProcessingSeconds);

/// <summary>The default: analytics off.</summary>
public sealed class NullAnalytics : IAnalytics
{
    public static readonly NullAnalytics Instance = new();

    private NullAnalytics()
    {
    }

    public bool IsEnabled => false;

    public void TakeAnalyzed(in TakeAnalyzedEvent e)
    {
    }
}

/// <summary>
/// What a take's event may contain: the whitelist. Durations are rounded (the take to 5 s, the
/// processing to 0.1 s) and each metric is reduced to the name of its zone (the registry's
/// <see cref="MetricInfo.Zones"/>), so no raw Hz or dB value leaves the device.
/// </summary>
public static class AnalyticsPayload
{
    /// <summary>The take's length, rounded to 5 s.</summary>
    public static int DurationBucket(Recording take) => (int)(Math.Round((take.DurationS ?? 0) / 5) * 5);

    public static double ProcessingRounded(double seconds) => Math.Round(seconds, 1);

    /// <summary>Each zoned metric the take has a value for, in registry order: its name and the name of its zone.</summary>
    public static IReadOnlyList<(string Metric, string Zone)> Zones(Recording take, MetricRegistry registry)
    {
        var zones = new List<(string, string)>();
        foreach (var m in registry.Metrics)
        {
            if (m.Info.Zones is not { Count: > 0 } list || m.Info.Take?.Invoke(take) is not { } value || !double.IsFinite(value)) continue;
            zones.Add((m.Info.Name, ZoneOf(list, value).Name));
        }
        return zones;
    }

    /// <summary>The zone holding <paramref name="value"/>; below the first counts as the first, above the last as the last.</summary>
    private static Zone ZoneOf(IReadOnlyList<Zone> zones, double value)
    {
        foreach (var z in zones)
            if (value < z.To) return z;
        return zones[^1];
    }
}
