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
    /// <summary>
    /// When it was recorded (or imported: the file's time), ISO 8601 with the UTC offset, e.g.
    /// "2026-10-07T14:03:12+03:00". Null for takes saved before it was kept (then only <see cref="Date"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordedAt { get; set; }

    /// <summary>"2026-10-07T14:03:12+03:00" for a local time.</summary>
    public static string Timestamp(DateTime local) =>
        new DateTimeOffset(local).ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
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
    /// <summary>
    /// How the formants were measured: "path" (the ceiling followed the voice, v0.4.0 and later);
    /// null for takes measured at the fixed 5500/5000 Hz of older versions (<see cref="PathMethod"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; set; }

    /// <summary>The value of <see cref="Method"/> for the ceiling path.</summary>
    public const string PathMethod = "path";

    public double? F1Hz { get; set; }
    public double? F2Hz { get; set; }
    public double? F3Hz { get; set; }
    /// <summary>
    /// Share (%) of the sampled loud voiced moments that gave a usable F1 (250–1000 Hz, 5500 Hz
    /// ceiling). Clean speech is above ~90 %; a low value means the formants were unreliable
    /// (noise, hiss, a very close mic). Null when too few moments to say, and in older takes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? UsablePct { get; set; }

    /// <summary>
    /// The formant ceiling (Hz) of most of the take, when the ceiling followed the voice (v0.4.0 and
    /// later); null for takes analysed with the fixed 5500/5000 Hz choice of older versions.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? CeilingHz { get; set; }

    /// <summary>The lowest and highest ceilings (Hz) the take used, and how often it changed (with <see cref="CeilingHz"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? CeilingLowestHz { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? CeilingHighestHz { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? CeilingSwitches { get; set; }
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
