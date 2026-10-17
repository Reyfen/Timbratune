namespace Reyfen.Timbratune.Core.Domain;

// What the charts draw: plain data, so view models build it without depending on the controls.

/// <summary>One point of a trend chart: its x-axis label (e.g. the phrase number) and value (null = not measured).</summary>
public readonly record struct TrendPoint(string Label, double? Y, string? Tooltip = null);

/// <summary>Who a <see cref="ScaleTick"/> stands for.</summary>
public enum TickKind { Take, Fem, Masc }

/// <summary>One marker on the reference scale — a take (above the band) or a reference voice (below).</summary>
public sealed record ScaleTick(
    string Key,
    double Value,
    double Pct,
    string Label,
    TickKind Kind,
    bool Active,
    int Lane,
    string? AudioPath,
    string PlayTitle,
    string Tooltip);
