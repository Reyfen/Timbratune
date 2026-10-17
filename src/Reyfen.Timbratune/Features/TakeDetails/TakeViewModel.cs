using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Metrics.Resonance;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Shared;
using static Reyfen.Timbratune.Core.Domain.Zones;

namespace Reyfen.Timbratune.Features.TakeDetails;

/// <summary>
/// Everything shown for the take in focus: the stat-card grid, resonance and
/// register sections. Copy is ported from App.tsx, ResonanceCard.tsx and
/// RegisterSection.tsx (**bold** / _italic_ are rendered by Controls.Markup).
/// While recording, one instance follows the take: <see cref="Update"/> changes its
/// values in place, so the view keeps its controls and only re-lays out the texts that
/// changed (rebuilding the whole tree each refresh took ~120 ms on a Pixel 4a).
/// The stat cards and trend charts are the registry's metrics that have one, in registry order.
/// </summary>
public sealed partial class TakeViewModel : ObservableObject
{
    private readonly bool _isLatest;
    private readonly IReadOnlyList<RegisteredMetric> _trends;

    /// <param name="liveElapsed">Set while recording: the take so far, shown live (no modal, no saved-take details).</param>
    public TakeViewModel(Recording r, RecordingDetail? detail, bool isLatest, IRelayCommand<MetricId> openMetric, IFeatureFlags flags,
        MetricRegistry registry, double? liveElapsed = null)
    {
        _isLatest = isLatest;
        IsLive = liveElapsed is not null;
        CompareHint = AppInfo.CompareHint(flags.ReferenceVoices);
        var hint = CompareHint;
        _trends = registry.Metrics.Where(m => m.Info.Trend is not null && m.Info.Columns.HasFlag(MetricColumns.Trend)).ToList();
        // Live cards don't open the reference modal.
        StatCards = registry.Metrics.Where(m => m.Info.Card is not null)
            .Select(m => new StatCardViewModel(m.Info, !IsLive && m.Info.Compare is not null ? m.Id : null, openMetric, hint))
            .ToList();
        Size = Gauge(ResonanceModule.Size, "Perceived size", "main resonance cue", "← smaller / lighter", "larger / deeper →");
        F2 = Gauge(ResonanceModule.F2, "F2", "brightness cue · depends on the words");
        F3 = Gauge(ResonanceModule.F3, "F3", "supports brightness");
        Positions = [new PositionBar("phrase starts"), new PositionBar("mid-phrase"), new PositionBar("phrase endings")];
        Update(r, detail, liveElapsed);

        GaugeViewModel Gauge(MetricInfo metric, string name, string description,
            string low = "← deeper / larger tract", string high = "brighter / smaller tract →") =>
            new(name, description, metric, registry.Id(metric), openMetric, hint, !IsLive) { LowCaption = low, HighCaption = high };
    }

    /// <summary>Shows another state of the take (while recording: the take so far).</summary>
    public void Update(Recording r, RecordingDetail? detail, double? liveElapsed = null)
    {
        Recording = r;
        Title = liveElapsed is { } elapsed ? $"🔴 Live · {TimeSpan.FromSeconds(elapsed):m\\:ss}"
            : _isLatest ? "Latest take" : $"Take #{r.Id}";
        When = liveElapsed is null ? WhenText(r) : "";

        foreach (var card in StatCards) card.Update(r);

        // ---- Resonance (ResonanceCard.tsx) ----
        Size.Update(r);
        F2.Update(r);
        F3.Update(r);
        var score = new[] { Size.Zone, F2.Zone, F3.Zone }.Sum(z => z?.Name is "bright" or "smaller" ? 1 : z?.Name is "deeper" or "larger" ? -1 : 0);
        ResonanceSummary = score >= 1
            ? "✨ Your resonance **leans bright & light** — this is the cue that makes a voice read feminine beyond pitch. Lovely!"
            : score <= -1
                ? "✨ Your resonance **leans deeper** right now — totally workable! This is usually the biggest lever after pitch."
                : "💫 Your resonance sits in a **neutral** zone — nudging brightness up will help it read lighter.";
        F1Line = $"ℹ️ **F1 = {Fmt(r.Formants.F1Hz)} Hz.** This one mostly reflects which _vowel_ you're on " +
                 "(mouth openness), so it's not a reliable gender cue by itself — shown for completeness." +
                 $"\n\n{Capitalized(ResonanceModule.CeilingNote(r))}." +
                 (r.Formants.UsablePct is < ResonanceModule.UnreliableFormantsBelowPct
                     ? $"\n\n⚠️ **Formants are shaky in this take:** only {Fmt(r.Formants.UsablePct)}% of the loud, voiced moments " +
                       "gave a usable reading. Background noise, hiss or a very close mic can cause this — try a quieter room " +
                       "or a bit more distance, and treat these resonance numbers with care."
                     : "");

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
            UpdateTrends(trends.Points, detail.RegisterFloorHz, detail.Trends?.Version ?? 0);
        }
    }

    /// <param name="version">The saved trends' version; 0 = rebuilt from the contour (an older take, being re-analyzed).</param>
    private void UpdateTrends(List<TrendSlice> slices, double floor, int version)
    {
        var charts = PhraseTrends.Count == 0 ? new List<TrendViewModel>(_trends.Count) : null;
        for (var i = 0; i < _trends.Count; i++)
        {
            var (id, info) = (_trends[i].Id, _trends[i].Info);
            var trend = info.Trend!;
            var unit = info.Unit == "%" ? "%" : " " + info.Unit;
            IReadOnlyList<TrendPoint> points = slices
                .Select(s => new TrendPoint($"{s.T:0}", s[id], $"{s.T:0} s ({s.Start:0.0}–{s.End:0.0} s): {Fmt(s[id], unit)}"))
                .ToList();
            var caption = (trend.FloorCaption?.Invoke(floor) ?? trend.Caption) + (version < trend.MeasuredSinceVersion ? " · measuring…" : "");
            var bands = trend.Band is not null ? null : trend.FloorBands?.Invoke(floor) ?? info.Zones;
            if (charts is null) PhraseTrends[i].Update(caption, points, bands);
            else if (trend.Band is { } band)
                charts.Add(new TrendViewModel(info.Title, caption, points, trend.ColorKey, bandFrom: band.From, bandTo: band.To, bandColorKey: band.ColorKey));
            else charts.Add(new TrendViewModel(info.Title, caption, points, trend.ColorKey, bands: bands));
        }
        if (charts is not null) PhraseTrends = charts;
    }

    [ObservableProperty] private Recording _recording = null!;
    public bool IsLive { get; }

    /// <summary>What tapping a metric card offers (depends on whether reference voices are built in).</summary>
    public string CompareHint { get; }
    /// <summary>The register section's contour; live, it's shown above the cards with zone bands instead.</summary>
    public bool ShowContour => !IsLive;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasWhen))] private string _title = "";

    /// <summary>When the take was recorded, in the device's date and time format (empty while live).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasWhen))] private string _when = "";
    public bool HasWhen => When.Length > 0;

    /// <summary>"08-Oct-26 · 14:03": the take's date and time in the device's formats, or just its date for takes without a time.</summary>
    private static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string WhenText(Recording r)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        if (DateTimeOffset.TryParse(r.RecordedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var at))
            return $"{at.LocalDateTime.ToString("d", culture)} · {at.LocalDateTime.ToString("t", culture)}";
        if (DateTime.TryParseExact(r.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day))
            return day.ToString("d", culture);
        return r.Date;
    }
    public IReadOnlyList<StatCardViewModel> StatCards { get; }

    public GaugeViewModel Size { get; }
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

/// <summary>One stat card: a metric's value, zone and line (<see cref="MetricInfo.Card"/>).</summary>
/// <param name="compareId">The metric to compare when tapped; null = not tappable.</param>
public sealed partial class StatCardViewModel(
    MetricInfo metric, MetricId? compareId, IRelayCommand<MetricId> openMetric, string compareHint) : ObservableObject
{
    public string Title { get; } = metric.Title;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText), nameof(Zone))]
    private double? _value;
    public string ValueText => Fmt(Value);
    public string Unit { get; } = metric.Unit;
    public IReadOnlyList<Zone>? Zones { get; } = metric.Zones;
    public bool HasZones => Zones is not null;
    public Zone? Zone => Zones is null ? null : ZoneOf(Zones, Value);
    public double Lo { get; } = metric.Lo;
    public double Hi { get; } = metric.Hi;
    [ObservableProperty] private string _sub = "";
    public bool IsClickable => compareId is not null;
    public string? ToolTip => IsClickable ? compareHint : null;

    public void Update(Recording take)
    {
        Value = metric.Take?.Invoke(take);
        Sub = metric.Card?.Sub(take) ?? "";
    }

    public void Open()
    {
        if (compareId is { } id) openMetric.Execute(id);
    }
}

public sealed partial class GaugeViewModel(
    string name, string description, MetricInfo metric, MetricId id, IRelayCommand<MetricId> openMetric, string compareHint,
    bool interactive = true) : ObservableObject
{
    public bool Interactive { get; } = interactive;
    public string? ToolTip => Interactive ? compareHint : null;
    public string Name { get; } = name;
    public string Description { get; } = description;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText), nameof(Zone))]
    private double? _value;
    public string ValueText => Fmt(Value, " " + metric.Unit);
    /// <summary>The captions under the bar's low and high ends.</summary>
    public string LowCaption { get; init; } = "← deeper / larger tract";
    public string HighCaption { get; init; } = "brighter / smaller tract →";
    public IReadOnlyList<Zone> Zones { get; } = metric.Zones ?? [];
    public Zone? Zone => Zones.Count == 0 ? null : ZoneOf(Zones, Value);
    public double Lo { get; } = metric.Lo;
    public double Hi { get; } = metric.Hi;

    public void Update(Recording take) => Value = metric.Take?.Invoke(take);

    public void Open()
    {
        if (Interactive) openMetric.Execute(id);
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
