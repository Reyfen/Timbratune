using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Controls;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Domain.Zones;

namespace Reyfen.Timbratune.ViewModels;

/// <summary>
/// Everything shown for the take in focus: the stat-card grid, resonance and
/// register sections. Copy is ported from App.tsx, ResonanceCard.tsx and
/// RegisterSection.tsx (**bold** / _italic_ are rendered by Controls.Markup).
/// </summary>
public sealed class TakeViewModel
{
    /// <param name="liveElapsed">Set while recording: the take so far, shown live (no modal, no saved-take details).</param>
    public TakeViewModel(Recording r, RecordingDetail? detail, bool isLatest, IRelayCommand<MetricKey> openMetric, double? liveElapsed = null)
    {
        Recording = r;
        IsLive = liveElapsed is not null;
        Title = liveElapsed is { } elapsed ? $"🔴 Live · {TimeSpan.FromSeconds(elapsed):m\\:ss}"
            : isLatest ? "Latest take" : $"Take #{r.Id}";
        MetricKey? Card(MetricKey key) => IsLive ? null : key; // live cards don't open the reference modal
        Banner = $"💗 #{r.Id} · {r.Label} · ";
        Date = r.Date;

        var pz = ZoneOf(Zones.Pitch, r.Pitch.MeanHz);
        StatCards =
        [
            new StatCardViewModel("Pitch (avg)", r.Pitch.MeanHz, "Hz", Zones.Pitch, 100, 260,
                pz is null ? "" : $"you're in the **{pz.Name}** zone — 165 Hz+ reads feminine to most ears 💕",
                Card(MetricKey.Pitch), openMetric),
            new StatCardViewModel("Pitch range", r.Pitch.RangeHz, "Hz", null, 0, 0,
                $"{Fmt(r.Pitch.MinHz)}–{Fmt(r.Pitch.MaxHz)} Hz · wider = more melodic & expressive",
                null, openMetric),
            new StatCardViewModel("Loudness", r.Intensity.MeanDb, "dB", Zones.Loudness, 45, 78,
                "louder = more present & confident 📣", Card(MetricKey.Loudness), openMetric),
            new StatCardViewModel("Pitch variability", r.Pitch.SdHz, "Hz", Zones.PitchSd, 0, 60,
                "how much your melody moves · ~20–40 Hz is lively, natural speech", Card(MetricKey.Sd), openMetric),
            new StatCardViewModel("Clarity (HNR)", r.VoiceQuality.HnrDb, "dB", Zones.Hnr, 0, 30,
                "higher = clearer, lower = breathier · runs lower on full passages than a held vowel",
                Card(MetricKey.Hnr), openMetric),
            new StatCardViewModel("Steadiness (jitter)", r.VoiceQuality.JitterPct, "%", Zones.Jitter, 0, 3,
                $"lower = steadier · shimmer {Fmt(r.VoiceQuality.ShimmerPct)}% (under ~3.8% is steady)",
                Card(MetricKey.Jitter), openMetric),
            new StatCardViewModel("Weight", r.Weight?.H1a3cDb, "dB", Zones.Weight, 0, 20,
                "source spectral tilt (corrected H1*–A3*) — the _thickness_ of the voice itself (separate from " +
                "pitch & resonance) · lighter leans feminine. **Heads up:** weight is hard to pin to a gender " +
                "across people (lots of overlap), so it's most useful as **your own change over time**, not a " +
                "vs-others verdict.",
                Card(MetricKey.Weight), openMetric),
        ];

        // ---- Resonance (ResonanceCard.tsx) ----
        F2 = new GaugeViewModel("F2", "main brightness cue", r.Formants.F2Hz, Zones.F2, 1100, 2000, MetricKey.F2, openMetric, !IsLive);
        F3 = new GaugeViewModel("F3", "supports brightness", r.Formants.F3Hz, Zones.F3, 2100, 3400, MetricKey.F3, openMetric, !IsLive);
        var score = new[] { F2.Zone, F3.Zone }.Sum(z => z?.Name == "bright" ? 1 : z?.Name == "deeper" ? -1 : 0);
        ResonanceSummary = score >= 1
            ? "✨ Your resonance **leans bright & light** — this is the cue that makes a voice read feminine beyond pitch. Lovely!"
            : score <= -1
                ? "✨ Your resonance **leans deeper** right now — totally workable! This is usually the biggest lever after pitch."
                : "💫 Your resonance sits in a **neutral** zone — nudging brightness up will help it read lighter.";
        F1Line = $"ℹ️ **F1 = {Fmt(r.Formants.F1Hz)} Hz.** This one mostly reflects which _vowel_ you're on " +
                 "(mouth openness), so it's not a reliable gender cue by itself — shown for completeness.";

        // ---- Register & phrasing (RegisterSection.tsx) ----
        Detail = detail;
        var reg = r.Register;
        HasRegister = reg is not null && detail is not null;
        if (reg is not null)
        {
            RegisterIntro = "Pitch isn't just an average — it's a **contour** that moves through every phrase. This " +
                            $"shows where your voice **falls out of register** (crashes below {Fmt(reg.FloorHz)} Hz, back " +
                            "toward chest voice). The **blue stretches** are the drops; watch where they cluster.";
            InRegisterText = Fmt(reg.InRegisterPct);
            LandedText = Fmt(reg.PhrasesLandedPct);
            LandedSub = $"of your {reg.NPhrases} phrases ended _in_ register (didn't trail off down low)";
            MelodyText = Fmt(reg.InRegisterSemitonesSd);
            MelodySub = $"in-register pitch variation. Raw looks like {Fmt(reg.SemitonesSd)} st, but the extra is " +
                        "register crashes, not melody — this is the honest number.";
            Positions =
            [
                new PositionBar("phrase starts", reg.OnsetSubPct),
                new PositionBar("mid-phrase", reg.MidSubPct),
                new PositionBar("phrase endings", reg.OffsetSubPct),
            ];
            // Stable sort by value, descending — ties keep onset → mid → offset order, like Array.sort.
            var worst = Positions.OrderByDescending(p => p.Value ?? 0).First();
            RegisterTip = $"🎯 Your weakest spot is **{worst.Label}** ({Fmt(worst.Value)}% sub-register). The most " +
                          "clockable goal: **land every phrase ending up in register** — don't let the last word " +
                          "trail down into chest voice.";
        }

        // ---- Trends within this take: ~10 slices of whole seconds ----
        if (detail is not null)
        {
            // Takes analyzed before the time trends existed only have the contour:
            // pitch-based trends are rebuilt here, and MainViewModel re-analyzes
            // the audio in the background to fill in the rest.
            var trends = detail.Trends ?? AnalysisPostProcessor.TimeTrends(detail);
            TrendStep = trends.StepS;
            TrendPointCount = trends.Points.Count;
            PhraseTrends = BuildTrends(trends.Points, detail.RegisterFloorHz, detail.Trends is null,
                (detail.Trends?.Version ?? 0) < TakeTrends.CurrentVersion);
        }
    }

    /// <param name="legacy">Only the contour is known yet (older take, being re-analyzed).</param>
    /// <param name="older">Saved before loudness was in the trends (also being re-analyzed).</param>
    private static IReadOnlyList<TrendViewModel> BuildTrends(List<TrendSlice> slices, double floor, bool legacy, bool older)
    {
        IReadOnlyList<TrendPoint> Mk(Func<TrendSlice, double?> sel, string unit) => slices
            .Select(s => new TrendPoint($"{s.T:0}", sel(s), $"{s.T:0} s ({s.Start:0.0}–{s.End:0.0} s): {Fmt(sel(s), unit)}"))
            .ToList();
        var reanalyze = legacy ? " · measuring…" : "";
        var reanalyzeLoudness = older ? " · measuring…" : "";

        return
        [
            new("Pitch (avg)", "pink band = feminine zone (165 Hz+)", Mk(s => s.MeanHz, " Hz"), "Chart1",
                bandFrom: Metrics.FemininePitchHz, bandTo: 260, bandColorKey: "ZoneFem"),
            new("Pitch variability", "how much your melody moves · ~20–40 Hz is lively, natural speech",
                Mk(s => s.PitchSdHz, " Hz"), "Chart9", bands: Zones.PitchSd),
            new("In-register melody", "true expressiveness, crashes removed (st)",
                Mk(s => s.MelodySt, " st"), "Chart2", bands: Zones.Melody),
            new("Phrase endings", $"pitch of the phrases ending here · blue = below the register floor ({Fmt(floor)} Hz)",
                Mk(s => s.OffsetHz, " Hz"), "Chart3",
                bands: [new Zone(floor - 50, floor, ZoneColorKey.Masc, "below"), new Zone(floor, 260, ZoneColorKey.Fem, "in register")]),
            new("Loudness", "louder = more present & confident" + reanalyzeLoudness, Mk(s => s.LoudnessDb, " dB"),
                "Chart10", bands: Zones.Loudness),
            new("Resonance (F2)", "brightness / vocal-tract size cue" + reanalyze, Mk(s => s.F2Hz, " Hz"), "Chart4",
                bands: Zones.F2),
            new("Resonance (F3)", "supports brightness" + reanalyze, Mk(s => s.F3Hz, " Hz"), "Chart6",
                bands: Zones.F3),
            new("Weight", "spectral tilt · lower = lighter / more feminine" + reanalyze, Mk(s => s.WeightDb, " dB"),
                "Chart5", bands: Zones.Weight),
            new("Clarity (HNR)", "higher = clearer, lower = breathier" + reanalyze, Mk(s => s.HnrDb, " dB"),
                "Chart7", bands: Zones.Hnr),
            new("Steadiness (jitter)", "lower = steadier" + reanalyze, Mk(s => s.JitterPct, "%"),
                "Chart8", bands: Zones.Jitter),
        ];
    }

    public Recording Recording { get; }
    public bool IsLive { get; }
    /// <summary>The register section's contour; live, it's shown above the cards with zone bands instead.</summary>
    public bool ShowContour => !IsLive;
    public string Title { get; }
    public string Banner { get; }
    public string Date { get; }
    public IReadOnlyList<StatCardViewModel> StatCards { get; }

    public GaugeViewModel F2 { get; }
    public GaugeViewModel F3 { get; }
    public string ResonanceSummary { get; }
    public string F1Line { get; }

    public RecordingDetail? Detail { get; }
    public bool HasRegister { get; }
    public bool NoRegister => !HasRegister;
    public string RegisterIntro { get; } = "";
    public string InRegisterText { get; } = "—";
    public string LandedText { get; } = "—";
    public string LandedSub { get; } = "";
    public string MelodyText { get; } = "—";
    public string MelodySub { get; } = "";
    public IReadOnlyList<PositionBar> Positions { get; } = [];
    public string RegisterTip { get; } = "";

    public double TrendStep { get; }
    public int TrendPointCount { get; }
    public IReadOnlyList<TrendViewModel> PhraseTrends { get; } = [];
    public bool ShowPhraseTrends => TrendPointCount >= 2;
    public string PhraseTrendsHint => TrendPointCount switch
    {
        0 => "this take is too short for trends yet ✨",
        1 => "this take is too short for trends — they need at least two points ✨",
        _ => $"a point every {TrendStep:0} s · each covers the {TrendStep:0} s around it · hover a dot for its time range",
    };
}

public sealed class TrendViewModel(
    string title, string caption, IReadOnlyList<TrendPoint> points, string lineColorKey,
    double? bandFrom = null, double? bandTo = null, string bandColorKey = "Accent", IReadOnlyList<Zone>? bands = null)
{
    public string Title { get; } = title;
    public string Caption { get; } = caption;
    public IReadOnlyList<TrendPoint> Points { get; } = points;
    public string LineColorKey { get; } = lineColorKey;
    public double? BandFrom { get; } = bandFrom;
    public double? BandTo { get; } = bandTo;
    public string BandColorKey { get; } = bandColorKey;
    public IReadOnlyList<Zone>? Bands { get; } = bands;
}

public sealed class StatCardViewModel(
    string title, double? value, string unit, IReadOnlyList<Zone>? zones, double lo, double hi,
    string sub, MetricKey? metricKey, IRelayCommand<MetricKey> openMetric)
{
    public string Title { get; } = title;
    public double? Value { get; } = value;
    public string ValueText { get; } = Fmt(value);
    public string Unit { get; } = unit;
    public IReadOnlyList<Zone>? Zones { get; } = zones;
    public bool HasZones => Zones is not null;
    public Zone? Zone { get; } = zones is null ? null : ZoneOf(zones, value);
    public double Lo { get; } = lo;
    public double Hi { get; } = hi;
    public string Sub { get; } = sub;
    public bool IsClickable => metricKey is not null;
    public string? ToolTip => IsClickable ? Features.CompareHint : null;

    public void Open()
    {
        if (metricKey is { } key) openMetric.Execute(key);
    }
}

public sealed class GaugeViewModel(
    string name, string description, double? value, IReadOnlyList<Zone> zones, double lo, double hi,
    MetricKey key, IRelayCommand<MetricKey> openMetric, bool interactive = true)
{
    public bool Interactive { get; } = interactive;
    public string? ToolTip => Interactive ? Features.CompareHint : null;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public double? Value { get; } = value;
    public string ValueText { get; } = Fmt(value, " Hz");
    public IReadOnlyList<Zone> Zones { get; } = zones;
    public Zone? Zone { get; } = ZoneOf(zones, value);
    public double Lo { get; } = lo;
    public double Hi { get; } = hi;

    public void Open()
    {
        if (Interactive) openMetric.Execute(key);
    }
}

/// <summary>One bar of "Where the register drops happen": height = clamp(v·3, 4, 100) % of the track.</summary>
public sealed class PositionBar(string label, double? value)
{
    public const double TrackHeight = 110;
    public string Label { get; } = label;
    public double? Value { get; } = value;
    public string ValueText { get; } = Fmt(value) + "%";
    public double BarHeight { get; } = Math.Clamp((value ?? 0) * 3, 4, 100) / 100 * TrackHeight;
}
