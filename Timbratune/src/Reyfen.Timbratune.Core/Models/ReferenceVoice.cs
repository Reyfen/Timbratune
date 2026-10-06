namespace Reyfen.Timbratune.Core.Models;

// One entry of reference.json — a real (VCTK, CC BY 4.0) speaker measured with
// the same method as the takes. Used only by the metric reference modal.

public sealed class ReferenceVoice
{
    public string Label { get; set; } = "";
    /// <summary>"f" or "m".</summary>
    public string Gender { get; set; } = "";
    public string Source { get; set; } = "";
    /// <summary>Relative path such as "reference-audio/vctk_f294.mp3".</summary>
    public string? Audio { get; set; }
    public Pitch Pitch { get; set; } = new();
    public Formants? Formants { get; set; }
    public Intensity Intensity { get; set; } = new();
    public VoiceQuality VoiceQuality { get; set; } = new();
    public Weight Weight { get; set; } = new();

    public bool IsFemale => Gender == "f";
}
