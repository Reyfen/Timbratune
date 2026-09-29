namespace Euphonia.Core.Models;

// Mirrors analysis/<id>.json (RecordingDetail in dashboard-react/src/types.ts).

public sealed class RecordingDetail
{
    public double RegisterFloorHz { get; set; }
    public double SemitoneRefHz { get; set; } = 100.0;
    public double DurationS { get; set; }
    public double TimeStep { get; set; } = 0.01;
    public Frames Frames { get; set; } = new();
    public List<Phrase> Phrases { get; set; } = [];
    public Register Summary { get; set; } = new();

    /// <summary>
    /// Per-phrase metrics for the "trends within this take" charts, aligned
    /// with <see cref="Phrases"/>. C#-port addition — absent in files written
    /// by analyze.py / older versions (the pitch-based values can then be
    /// recomputed from <see cref="Frames"/>; F2 and weight can't).
    /// </summary>
    public List<PhraseMetrics>? PhraseMetrics { get; set; }
}

public sealed class PhraseMetrics
{
    public double Start { get; set; }
    public double End { get; set; }
    /// <summary>Mean F0 over the phrase's voiced frames.</summary>
    public double? MeanHz { get; set; }
    /// <summary>Semitone SD over the phrase's in-register frames ("true melody").</summary>
    public double? MelodySt { get; set; }
    /// <summary>Mean F0 over the last 0.12 s (same as <see cref="Phrase.OffsetHz"/>).</summary>
    public double? OffsetHz { get; set; }
    /// <summary>Median F2 of the gated vowel-core frames inside the phrase.</summary>
    public double? F2Hz { get; set; }
    /// <summary>Mean corrected H1*–A3* of the weight frames inside the phrase.</summary>
    public double? WeightDb { get; set; }
}

public sealed class Frames
{
    public List<double> T { get; set; } = [];
    /// <summary>Null where the frame is unvoiced.</summary>
    public List<double?> Hz { get; set; } = [];
}

public sealed class Phrase
{
    public double Start { get; set; }
    public double End { get; set; }
    public double OnsetHz { get; set; }
    public double OffsetHz { get; set; }
    public double MinHz { get; set; }
    public bool StartedInRegister { get; set; }
    public bool EndedInRegister { get; set; }
    public double SubRegisterPct { get; set; }
}
