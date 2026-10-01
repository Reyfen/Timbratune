using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Euphonia.Controls;
using Euphonia.Core.Domain;
using Euphonia.Core.Models;
using Euphonia.Services;

namespace Euphonia.ViewModels;

/// <summary>
/// MetricModal.tsx: a metric's reference scale with every take (dots in
/// lanes above the band) and — for gendered metrics — the real reference
/// voices (ticks below). Clicking anything with audio plays it here.
/// </summary>
public sealed partial class MetricModalViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly Action _close;

    public MetricModalViewModel(MetricDef metric, IReadOnlyList<Recording> recordings,
        IReadOnlyList<ReferenceVoice> references, int? activeId, Func<Recording, string?> takeAudio,
        Func<ReferenceVoice, string?> refAudio, PlaybackService playback, Action close)
    {
        Metric = metric;
        _playback = playback;
        _close = close;
        _playback.PropertyChanged += OnPlaybackChanged;

        // Stretch lo/hi so every real value fits, then pad 6% (or 1 if the span is 0).
        double lo = metric.Lo, hi = metric.Hi;
        var values = recordings.Select(metric.Take)
            .Concat(metric.ShowRefs ? references.Select(metric.Ref) : [])
            .Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value);
        foreach (var v in values)
        {
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
        }
        var pad = (hi - lo) * 0.06;
        if (pad == 0) pad = 1;
        Lo = lo - pad;
        Hi = hi + pad;
        var span = Hi - Lo;
        double Pct(double v) => Math.Clamp((v - Lo) / span * 100, 0, 100);

        var takes = new List<ScaleTick>();
        foreach (var r in recordings)
        {
            if (metric.Take(r) is not { } v || !double.IsFinite(v)) continue;
            var audio = takeAudio(r);
            takes.Add(new ScaleTick($"take-{r.Id}", v, Pct(v), $"#{r.Id}", TickKind.Take, r.Id == activeId, 0, audio,
                $"take #{r.Id} — {r.Label}",
                $"take #{r.Id} · {Zones.Fmt(Math.Round(v * 10) / 10)}{metric.Unit}{(audio is not null ? " · 🔊 click to hear it" : "")}"));
        }
        Takes = AssignLanes(takes, 16);

        Refs = !metric.ShowRefs
            ? []
            : references.Select((rv, i) => (rv, i))
                .Where(x => metric.Ref(x.rv) is { } v && double.IsFinite(v))
                .Select(x =>
                {
                    var v = metric.Ref(x.rv)!.Value;
                    var audio = refAudio(x.rv);
                    return new ScaleTick($"ref-{x.i}", v, Pct(v), x.rv.Label, x.rv.IsFemale ? TickKind.Fem : TickKind.Masc,
                        false, 0, audio, x.rv.Label,
                        $"{x.rv.Label} · {Zones.Fmt(Math.Round(v))}{metric.Unit}{(audio is not null ? " · 🔊 click to hear it" : "")}");
                })
                .ToList();
    }

    public MetricDef Metric { get; }
    public string Heading => $"{Metric.Title} reference scale";
    public double Lo { get; }
    public double Hi { get; }
    public IReadOnlyList<ScaleTick> Takes { get; }
    public IReadOnlyList<ScaleTick> Refs { get; }
    public bool ShowRefs => Features.ReferenceVoices && Metric.ShowRefs;
    public bool HideRefs => Features.ReferenceVoices && !Metric.ShowRefs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ScaleTick? _selected;

    public bool HasSelection => Selected is not null;
    public string PlayerTitle => Selected is null ? "" : "🎧 " + Selected.PlayTitle;
    // Plain glyphs, not emoji: ⏸ renders as a colour-emoji box on Windows.
    public string PlayGlyph => Selected?.AudioPath is { } p && _playback.CurrentPath == p && _playback.IsPlaying ? "❚❚" : "▶";
    public double Progress => Selected?.AudioPath is { } p && _playback.CurrentPath == p ? _playback.Progress : 0;
    public string? SelectedKey => Selected?.Key;

    [RelayCommand]
    private void Tick(ScaleTick tick)
    {
        if (tick.AudioPath is null) return;
        if (Selected?.Key == tick.Key)
        {
            ClosePlayer(); // click the same one again → close the player
            return;
        }
        Selected = tick;
        _playback.Play(tick.AudioPath);
        OnPropertyChanged(nameof(PlayerTitle));
        OnPropertyChanged(nameof(SelectedKey));
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (Selected?.AudioPath is { } p) _playback.Toggle(p);
    }

    [RelayCommand]
    private void Seek(double fraction)
    {
        if (Selected?.AudioPath is { } p) _playback.Seek(p, fraction);
    }

    [RelayCommand]
    private void ClosePlayer()
    {
        if (Selected?.AudioPath is { } p && _playback.CurrentPath == p) _playback.Stop();
        Selected = null;
        OnPropertyChanged(nameof(PlayerTitle));
        OnPropertyChanged(nameof(SelectedKey));
    }

    [RelayCommand]
    private void Close()
    {
        ClosePlayer();
        _close();
    }

    /// <summary>
    /// Greedy lanes: sort by position; each tick goes into the lowest lane whose
    /// last tick is at least <paramref name="minGapPct"/> to the left.
    /// </summary>
    internal static IReadOnlyList<ScaleTick> AssignLanes(List<ScaleTick> ticks, double minGapPct)
    {
        var laneRight = new List<double>();
        var result = new List<ScaleTick>(ticks.Count);
        foreach (var t in ticks.OrderBy(t => t.Pct))
        {
            var lane = laneRight.FindIndex(r => t.Pct - r >= minGapPct);
            if (lane == -1)
            {
                lane = laneRight.Count;
                laneRight.Add(t.Pct);
            }
            else laneRight[lane] = t.Pct;
            result.Add(t with { Lane = lane });
        }
        return result;
    }

    private void OnPlaybackChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(Progress));
    }

    public void Dispose() => _playback.PropertyChanged -= OnPlaybackChanged;
}
