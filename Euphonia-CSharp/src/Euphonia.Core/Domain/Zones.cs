using System.Globalization;

namespace Euphonia.Core.Domain;

// Port of dashboard-react/src/zones.ts — reference zones for voice
// feminization (gentle guidance, not law).
//
// CRITICAL COLOR CONVENTION (the meaning is fixed; the actual color per key
// comes from the active theme's Zone* brushes):
//   Masc    = MASCULINE / deeper gendered end ONLY (never "needs work")
//   Fem     = good / feminine end
//   Neutral = neutral / mid ("butter")
//   Grow    = neutral "room to grow" for skill metrics (breathy/rough/flat)
//   Soft / Comfy / Strong = loudness only

public enum ZoneColorKey { Masc, Fem, Neutral, Grow, Soft, Comfy, Strong }

/// <summary>A zone covers [From, To).</summary>
public sealed record Zone(double From, double To, ZoneColorKey Color, string Name);

public static class Zones
{
    public static readonly IReadOnlyList<Zone> Pitch =
    [
        new(100, 145, ZoneColorKey.Masc, "masc"),
        new(145, 165, ZoneColorKey.Neutral, "neutral"),
        new(165, 260, ZoneColorKey.Fem, "fem"),
    ];

    // Resonance — grounded on VCTK American speakers (see zones.ts for the
    // derivation). Blue = deeper (lower) only; pink = brighter (higher).
    public static readonly IReadOnlyList<Zone> F2 =
    [
        new(1100, 1340, ZoneColorKey.Masc, "deeper"),
        new(1340, 1420, ZoneColorKey.Neutral, "neutral"),
        new(1420, 2200, ZoneColorKey.Fem, "bright"),
    ];

    public static readonly IReadOnlyList<Zone> F3 =
    [
        new(2100, 2560, ZoneColorKey.Masc, "deeper"),
        new(2560, 2700, ZoneColorKey.Neutral, "neutral"),
        new(2700, 3400, ZoneColorKey.Fem, "bright"),
    ];

    public static readonly IReadOnlyList<Zone> Loudness =
    [
        new(45, 55, ZoneColorKey.Soft, "soft"),
        new(55, 65, ZoneColorKey.Comfy, "comfy"),
        new(65, 78, ZoneColorKey.Strong, "strong"),
    ];

    // SD of pitch — how much your melody moves. Higher = more expressive.
    public static readonly IReadOnlyList<Zone> PitchSd =
    [
        new(0, 20, ZoneColorKey.Grow, "flat"),
        new(20, 40, ZoneColorKey.Neutral, "natural"),
        new(40, 80, ZoneColorKey.Fem, "expressive"),
    ];

    // HNR — clarity vs. breathiness.
    public static readonly IReadOnlyList<Zone> Hnr =
    [
        new(0, 10, ZoneColorKey.Grow, "breathy"),
        new(10, 18, ZoneColorKey.Neutral, "clear-ish"),
        new(18, 35, ZoneColorKey.Fem, "clear"),
    ];

    // Jitter — LOWER is better, so the steady end is pink.
    public static readonly IReadOnlyList<Zone> Jitter =
    [
        new(0, 1, ZoneColorKey.Fem, "steady"),
        new(1, 2, ZoneColorKey.Neutral, "okay"),
        new(2, 6, ZoneColorKey.Grow, "rough"),
    ];

    // Vocal weight (corrected H1*–A3*): SMALLER = lighter (feminine). Weight
    // does not gender-separate reliably — read it as your own change over time.
    public static readonly IReadOnlyList<Zone> Weight =
    [
        new(0, 10, ZoneColorKey.Fem, "light"),
        new(10, 12.5, ZoneColorKey.Neutral, "overlap"),
        new(12.5, 20, ZoneColorKey.Masc, "heavy"),
    ];

    // In-register melody (semitones SD). Expressiveness, not gendered — no blue.
    public static readonly IReadOnlyList<Zone> Melody =
    [
        new(0, 2, ZoneColorKey.Grow, "flat"),
        new(2, 3.5, ZoneColorKey.Neutral, "natural"),
        new(3.5, 7, ZoneColorKey.Fem, "lively"),
    ];

    /// <summary>
    /// The zone containing <paramref name="v"/>, or null when v is null.
    /// </summary>
    /// <remarks>
    /// TODO(port): kept identical to zones.ts — a value outside every range
    /// (including BELOW the lowest one, e.g. 95 Hz pitch) falls back to the
    /// LAST zone. Fix in both apps together if that's ever changed.
    /// </remarks>
    public static Zone? ZoneOf(IReadOnlyList<Zone> zones, double? v)
    {
        if (v is not { } value) return null;
        foreach (var z in zones)
            if (value >= z.From && value < z.To) return z;
        return zones[^1];
    }

    /// <summary>zones.ts fmt(): "—" for null, else the number (unrounded) plus unit.</summary>
    public static string Fmt(double? v, string unit = "") =>
        v is { } value ? value.ToString(CultureInfo.InvariantCulture) + unit : "—";
}
