namespace Reyfen.Timbratune.Core.Models;

// Mirrors dashboard-react/src/types.ts and the entries analyze.py writes to
// recordings.json. Property order matches analyze.py's key order so the JSON
// on disk reads the same. Every metric is nullable: analyze.py's clean()
// turns NaN/inf into null, and legacy entries may lack weight/register.

public sealed class Recording
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string Note { get; set; } = "";
    public string Date { get; set; } = "";
    public string SourceFile { get; set; } = "";
    /// <summary>Relative to the data root, e.g. "audio/001.wav". Null when no audio was kept.</summary>
    public string? Audio { get; set; }
    /// <summary>Relative to the data root, e.g. "analysis/1.json".</summary>
    public string? Detail { get; set; }
    public double? DurationS { get; set; }
    public Pitch Pitch { get; set; } = new();
    public Formants Formants { get; set; } = new();
    public VoiceQuality VoiceQuality { get; set; } = new();
    public Intensity Intensity { get; set; } = new();
    public Weight? Weight { get; set; }
    public Register? Register { get; set; }
}

public sealed class Pitch
{
    public double? MeanHz { get; set; }
    public double? MedianHz { get; set; }
    public double? MinHz { get; set; }
    public double? MaxHz { get; set; }
    public double? RangeHz { get; set; }
    public double? SdHz { get; set; }
}

public sealed class Formants
{
    public double? F1Hz { get; set; }
    public double? F2Hz { get; set; }
    public double? F3Hz { get; set; }
}

public sealed class VoiceQuality
{
    public double? HnrDb { get; set; }
    public double? JitterPct { get; set; }
    public double? ShimmerPct { get; set; }
}

public sealed class Intensity
{
    public double? MeanDb { get; set; }
    public double? MinDb { get; set; }
    public double? MaxDb { get; set; }
}

public sealed class Weight
{
    /// <summary>Corrected H1*–A3* (Iseli–Alwan), dB. Lower = lighter.</summary>
    public double? H1a3cDb { get; set; }
    public double? H1a3Db { get; set; }
    public double? TiltDbKhz { get; set; }
}

public sealed class Register
{
    public double FloorHz { get; set; }
    public double? InRegisterPct { get; set; }
    public double? SemitonesSd { get; set; }
    public double? InRegisterSemitonesSd { get; set; }
    public double? OnsetSubPct { get; set; }
    public double? MidSubPct { get; set; }
    public double? OffsetSubPct { get; set; }
    public double? PhrasesLandedPct { get; set; }
    public int NPhrases { get; set; }
}
