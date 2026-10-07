using CommunityToolkit.Mvvm.ComponentModel;
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
/// While recording, one instance follows the take: <see cref="Update"/> changes its
/// values in place, so the view keeps its controls and only re-lays out the texts that
/// changed (rebuilding the whole tree each refresh took ~120 ms on a Pixel 4a).
/// </summary>
public sealed partial class TakeViewModel : ObservableObject
{
    private readonly bool _isLatest;
    private readonly IRelayCommand<MetricKey> _openMetric;

    /// <param name="liveElapsed">Set while recording: the take so far, shown live (no modal, no saved-take details).</param>
    public TakeViewModel(Recording r, RecordingDetail? detail, bool isLatest, IRelayCommand<MetricKey> openMetric, double? liveElapsed = null)
    {
        _isLatest = isLatest;
        _openMetric = openMetric;
        IsLive = liveElapsed is not null;
        MetricKey? Card(MetricKey key) => IsLive ? null : key; // live cards don't open the reference modal
        StatCards =
        [
            new StatCardViewModel("Pitch (avg)", "Hz", Zones.Pitch, 100, 260, Card(MetricKey.Pitch), openMetric),
            new StatCardViewModel("Pitch range", "Hz", null, 0, 0, null, openMetric),
            new StatCardViewModel("Loudness", "dB", Zones.Loudness, 45, 78, Card(MetricKey.Loudness), openMetric),
            new StatCardViewModel("Pitch variability", "Hz", Zones.PitchSd, 0, 60, Card(MetricKey.Sd), openMetric),
            new StatCardViewModel("Clarity (HNR)", "dB", Zones.Hnr, 0, 30, Card(MetricKey.Hnr), openMetric),
            new StatCardViewModel("Steadiness (jitter)", "%", Zones.Jitter, 0, 3, Card(MetricKey.Jitter), openMetric),
            new StatCardViewModel("Weight", "dB", Zones.Weight, 0, 20, Card(MetricKey.Weight), openMetric),
        ];
        F2 = new GaugeViewModel("F2", "main brightness cue", Zones.F2, 1100, 2000, MetricKey.F2, openMetric, !IsLive);
        F3 = new GaugeViewModel("F3", "supports brightness", Zones.F3, 2100, 3400, MetricKey.F3, openMetric, !IsLive);
        Positions = [new PositionBar("phrase starts"), new PositionBar("mid-phrase"), new PositionBar("phrase endings")];
        Update(r, detail, liveElapsed);
    }

    /// <summary>Shows another state of the take (while recording: the take so far).</summary>
    public void Update(Recording r, RecordingDetail? detail, double? liveElapsed = null)
    {
        Recording = r;
        Title = liveElapsed is { } elapsed ? $"🔴 Live · {TimeSpan.FromSeconds(elapsed):m\\:ss}"
            : _isLatest ? "Latest take" : $"Take #{r.Id}";
        Banner = $"💗 #{r.Id} · {r.Label} · ";
        Date = r.Date;

        var pz = ZoneOf(Zones.Pitch, r.Pitch.MeanHz);
        StatCards[0].Update(r.Pitch.MeanHz,
            pz is null ? "" : $"you're in the **{pz.Name}** zone — 165 Hz+ reads feminine to most ears 💕");
        StatCards[1].Update(r.Pitch.RangeHz, $"{Fmt(r.Pitch.MinHz)}–{Fmt(r.Pitch.MaxHz)} Hz · wider = more melodic & expressive");
        StatCards[2].Update(r.Intensity.MeanDb, "louder = more present & confident 📣");
        StatCards[3].Update(r.Pitch.SdHz, "how much your melody moves · ~20–40 Hz is lively, natural speech");
        StatCards[4].Update(r.VoiceQuality.HnrDb, "higher = clearer, lower = breathier · runs lower on full passages than a held vowel");
        StatCards[5].Update(r.VoiceQuality.JitterPct,
            $"lower = steadier · shimmer {Fmt(r.VoiceQuality.ShimmerPct)}% (under ~3.8% is steady)");
        StatCards[6].Update(r.Weight?.H1a3cDb,
            "source spectral tilt (corrected H1*–A3*) — the _thickness_ of the voice itself (separate from " +
            "pitch & resonance) · lighter leans feminine. **Heads up:** weight is hard to pin to a gender " +
            "across people (lots of overlap), so it's most useful as **your own change over time**, not a " +
            "vs-others verdict.");

        // ---- Resonance (ResonanceCard.tsx) ----
        F2.Update(r.Formants.F2Hz);
        F3.Update(r.Formants.F3Hz);
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
            Positions[0].Update(reg.OnsetSubPct);
            Positions[1].Update(reg.MidSubPct);
            Positions[2].Update(reg.OffsetSubPct);
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
            UpdateTrends(trends.Points, detail.RegisterFloorHz, detail.Trends is null,
                (detail.Trends?.Version ?? 0) < TakeTrends.CurrentVersion);
        }
    }

    /// <param name="legacy">Only the contour is known yet (older take, being re-analyzed).</param>
    /// <param name="older">Saved before loudness was in the trends (also being re-analyzed).</param>
    private void UpdateTrends(List<TrendSlice> slices, double floor, bool legacy, bool older)
    {
        IReadOnlyList<TrendPoint> Mk(Func<TrendSlice, double?> sel, string unit) => slices
            .Select(s => new TrendPoint($"{s.T:0}", sel(s), $"{s.T:0} s ({s.Start:0.0}–{s.End:0.0} s): {Fmt(sel(s), unit)}"))
            .ToList();
        var reanalyze = legacy ? " · measuring…" : "";
        var reanalyzeLoudness = older ? " · measuring…" : "";
        var endingBands = new Zone[] { new(floor - 50, floor, ZoneColorKey.Masc, "below"), new(floor, 260, ZoneColorKey.Fem, "in register") };

        (string Title, string Caption, IReadOnlyList<TrendPoint> Points, string Color, IReadOnlyList<Zone>? Bands)[] charts =
        [
            ("Pitch (avg)", "pink band = feminine zone (165 Hz+)", Mk(s => s.MeanHz, " Hz"), "Chart1", null),
            ("Pitch variability", "how much your melody moves · ~20–40 Hz is lively, natural speech", Mk(s => s.PitchSdHz, " Hz"), "Chart9", Zones.PitchSd),
            ("In-register melody", "true expressiveness, crashes removed (st)", Mk(s => s.MelodySt, " st"), "Chart2", Zones.Melody),
            ("Phrase endings", $"pitch of the phrases ending here · blue = below the register floor ({Fmt(floor)} Hz)",
                Mk(s => s.OffsetHz, " Hz"), "Chart3", endingBands),
            ("Loudness", "louder = more present & confident" + reanalyzeLoudness, Mk(s => s.LoudnessDb, " dB"), "Chart10", Zones.Loudness),
            ("Resonance (F2)", "brightness / vocal-tract size cue" + reanalyze, Mk(s => s.F2Hz, " Hz"), "Chart4", Zones.F2),
            ("Resonance (F3)", "supports brightness" + reanalyze, Mk(s => s.F3Hz, " Hz"), "Chart6", Zones.F3),
            ("Weight", "spectral tilt · lower = lighter / more feminine" + reanalyze, Mk(s => s.WeightDb, " dB"), "Chart5", Zones.Weight),
            ("Clarity (HNR)", "higher = clearer, lower = breathier" + reanalyze, Mk(s => s.HnrDb, " dB"), "Chart7", Zones.Hnr),
            ("Steadiness (jitter)", "lower = steadier" + reanalyze, Mk(s => s.JitterPct, "%"), "Chart8", Zones.Jitter),
        ];
        if (PhraseTrends.Count == 0)
        {
            PhraseTrends = charts.Select(c => c.Title == "Pitch (avg)"
                    ? new TrendViewModel(c.Title, c.Caption, c.Points, c.Color, bandFrom: Metrics.FemininePitchHz, bandTo: 260, bandColorKey: "ZoneFem")
                    : new TrendViewModel(c.Title, c.Caption, c.Points, c.Color, bands: c.Bands))
                .ToList();
            return;
        }
        for (var i = 0; i < charts.Length; i++) PhraseTrends[i].Update(charts[i].Caption, charts[i].Points, charts[i].Bands);
    }

    [ObservableProperty] private Recording _recording = null!;
    public bool IsLive { get; }
    /// <summary>The register section's contour; live, it's shown above the cards with zone bands instead.</summary>
    public bool ShowContour => !IsLive;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _banner = "";
    [ObservableProperty] private string _date = "";
    public IReadOnlyList<StatCardViewModel> StatCards { get; }

    public GaugeViewModel F2 { get; }
    public GaugeViewModel F3 { get; }
    [ObservableProperty] private string _resonanceSummary = "";
    [ObservableProperty] private string _f1Line = "";

    [ObservableProperty] private RecordingDetail? _detail;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRegister))]
    private bool _hasRegister;
    public bool NoRegister => !HasRegister;
    [ObservableProperty] private string _registerIntro = "";
    [ObservableProperty] private string _inRegisterText = "—";
    [ObservableProperty] private string _landedText = "—";
    [ObservableProperty] private string _landedSub = "";
    [ObservableProperty] private string _melodyText = "—";
    [ObservableProperty] private string _melodySub = "";
    public IReadOnlyList<PositionBar> Positions { get; }
    [ObservableProperty] private string _registerTip = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhraseTrendsHint))]
    private double _trendStep;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPhraseTrends), nameof(PhraseTrendsHint))]
    private int _trendPointCount;
    [ObservableProperty] private IReadOnlyList<TrendViewModel> _phraseTrends = [];
    public bool ShowPhraseTrends => TrendPointCount >= 2;
    public string PhraseTrendsHint => TrendPointCount switch
    {
        0 => "this take is too short for trends yet ✨",
        1 => "this take is too short for trends — they need at least two points ✨",
        _ => $"a point every {TrendStep:0} s · each covers the {TrendStep:0} s around it · hover a dot for its time range",
    };
}

public sealed partial class TrendViewModel(
    string title, string caption, IReadOnlyList<TrendPoint> points, string lineColorKey,
    double? bandFrom = null, double? bandTo = null, string bandColorKey = "Accent", IReadOnlyList<Zone>? bands = null)
    : ObservableObject
{
    public string Title { get; } = title;
    [ObservableProperty] private string _caption = caption;
    [ObservableProperty] private IReadOnlyList<TrendPoint> _points = points;
    public string LineColorKey { get; } = lineColorKey;
    public double? BandFrom { get; } = bandFrom;
    public double? BandTo { get; } = bandTo;
    public string BandColorKey { get; } = bandColorKey;
    [ObservableProperty] private IReadOnlyList<Zone>? _bands = bands;

    public void Update(string caption, IReadOnlyList<TrendPoint> points, IReadOnlyList<Zone>? bands)
    {
        Caption = caption;
        if (!points.SequenceEqual(Points)) Points = points; // unchanged points: no redraw
        if (bands is not null && (Bands is null || !bands.SequenceEqual(Bands))) Bands = bands;
    }
}

public sealed partial class StatCardViewModel(
    string title, string unit, IReadOnlyList<Zone>? zones, double lo, double hi,
    MetricKey? metricKey, IRelayCommand<MetricKey> openMetric) : ObservableObject
{
    public string Title { get; } = title;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText), nameof(Zone))]
    private double? _value;
    public string ValueText => Fmt(Value);
    public string Unit { get; } = unit;
    public IReadOnlyList<Zone>? Zones { get; } = zones;
    public bool HasZones => Zones is not null;
    public Zone? Zone => Zones is null ? null : ZoneOf(Zones, Value);
    public double Lo { get; } = lo;
    public double Hi { get; } = hi;
    [ObservableProperty] private string _sub = "";
    public bool IsClickable => metricKey is not null;
    public string? ToolTip => IsClickable ? Features.CompareHint : null;

    public void Update(double? value, string sub)
    {
        Value = value;
        Sub = sub;
    }

    public void Open()
    {
        if (metricKey is { } key) openMetric.Execute(key);
    }
}

public sealed partial class GaugeViewModel(
    string name, string description, IReadOnlyList<Zone> zones, double lo, double hi,
    MetricKey key, IRelayCommand<MetricKey> openMetric, bool interactive = true) : ObservableObject
{
    public bool Interactive { get; } = interactive;
    public string? ToolTip => Interactive ? Features.CompareHint : null;
    public string Name { get; } = name;
    public string Description { get; } = description;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText), nameof(Zone))]
    private double? _value;
    public string ValueText => Fmt(Value, " Hz");
    public IReadOnlyList<Zone> Zones { get; } = zones;
    public Zone? Zone => ZoneOf(Zones, Value);
    public double Lo { get; } = lo;
    public double Hi { get; } = hi;

    public void Update(double? value) => Value = value;

    public void Open()
    {
        if (Interactive) openMetric.Execute(key);
    }
}

/// <summary>One bar of "Where the register drops happen": height = clamp(v·3, 4, 100) % of the track.</summary>
public sealed partial class PositionBar(string label) : ObservableObject
{
    public const double TrackHeight = 110;
    public string Label { get; } = label;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText), nameof(BarHeight))]
    private double? _value;
    public string ValueText => Fmt(Value) + "%";
    public double BarHeight => Math.Clamp((Value ?? 0) * 3, 4, 100) / 100 * TrackHeight;

    public void Update(double? value) => Value = value;
}
