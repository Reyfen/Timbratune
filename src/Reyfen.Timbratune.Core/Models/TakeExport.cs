namespace Reyfen.Timbratune.Core.Models;

// take.json inside a .tmbr file (see Storage/TakeArchive): everything the take's
// dashboard shows, so a later version can import it without analyzing the audio again.

public sealed class TakeExport
{
    public const string FormatName = "timbratune-take";

    /// <summary>Version of this layout; bump when a field changes meaning or is removed.</summary>
    public const int CurrentExporterVersion = 1;

    public string Format { get; set; } = FormatName;
    public int ExporterVersion { get; set; } = CurrentExporterVersion;
    /// <summary>Timbratune version that wrote the file, e.g. "0.2.0".</summary>
    public string AppVersion { get; set; } = "";
    /// <summary>UTC, ISO 8601.</summary>
    public string ExportedAt { get; set; } = "";

    public ExportedTake Take { get; set; } = new();
    public ExportedAudio Audio { get; set; } = new();
    public AnalysisSettings Analysis { get; set; } = new();

    /// <summary>The take's metrics: the stat cards, resonance gauges and register cards.</summary>
    public Recording Metrics { get; set; } = new();

    /// <summary>The saved take: 10 ms pitch contour, phrases, register summary, trends.</summary>
    public RecordingDetail Detail { get; set; } = new();

    /// <summary>Per-frame lists: pitch, loudness, hnr, f1, f2, f3, weight, jitter.</summary>
    public TakeSeries Series { get; set; } = new();
}

public sealed class ExportedTake
{
    public int Id { get; set; }
    public string? Label { get; set; }
    public string? Note { get; set; }
    public string? Date { get; set; }
    public double? DurationS { get; set; }
}

public sealed class ExportedAudio
{
    /// <summary>Name of the WAV entry in the archive.</summary>
    public string File { get; set; } = "";
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
}

/// <summary>The analysis settings behind the numbers (frame spacing, ranges).</summary>
public sealed class AnalysisSettings
{
    public double PitchFloorHz { get; set; }
    public double PitchCeilingHz { get; set; }
    /// <summary>Pitch frame spacing (s); also the spacing of the formant, weight and contour points.</summary>
    public double PitchStepS { get; set; }
    public double IntensityStepS { get; set; }
    public double HnrStepS { get; set; }
    public double FormantCeilingHz { get; set; }
    public int FormantCount { get; set; }
    public double FormantWindowS { get; set; }
    public double FormantStepS { get; set; }
    public double RegisterFloorHz { get; set; }
    public double SemitoneRefHz { get; set; }
    /// <summary>Spacing of the trend points (s).</summary>
    public double? TrendStepS { get; set; }
}
