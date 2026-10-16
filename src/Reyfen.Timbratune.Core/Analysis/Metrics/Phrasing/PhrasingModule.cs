using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Phrasing;

/// <summary>
/// Phrasing and register: analyze_register() of analyze.py — the 10 ms contour as saved, the
/// phrases (sounding stretches with voiced frames: onset, offset, lowest pitch, whether they
/// started and ended in register), the time below the register floor by position in the phrase,
/// and the register headline; melody (in-register pitch SD) and phrase endings per phrase and per
/// trend slice. It makes the take's detail, which the other modules' columns are added to.
/// </summary>
public sealed class PhrasingModule : IMetricModule
{
    public string Name => "phrasing";

    public TrackSet RequiredTracks => TrackSet.Sounding;

    public void Measure(in TrackView t, RawAnalysisBuilder raw) => raw.Sounding = t.Derived.Sounding;

    public void Summarize(MetricContext context)
    {
        var raw = context.Raw;
        var (detail, register) = AnalyzeRegister(raw.Contour, raw.Sounding, raw.Get("duration"), context.RegisterFloorHz);
        context.Metrics.Register = register;
        context.Detail = detail;
    }

    public void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, PhraseMetrics metrics)
    {
        var inReg = inputs.WindowHz.Where(h => h >= inputs.Detail.RegisterFloorHz).Select(HzToSt).ToList();
        metrics.MelodySt = Clean(SampleSd(inReg));
        metrics.OffsetHz = phrase.OffsetHz;
    }

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, TrendSlice slice)
    {
        var w = window;
        var inReg = inputs.WindowHz.Where(h => h >= inputs.Detail.RegisterFloorHz).Select(HzToSt).ToList();
        var endings = inputs.Detail.Phrases.Where(p => w.Contains(p.End)).Select(p => p.OffsetHz).ToList();
        slice.MelodySt = Clean(SampleSd(inReg));
        slice.OffsetHz = endings.Count > 0 ? Round(endings.Average(), 1) : null;
    }

    // ------------------------------------------------------------------
    // analyze_register(): contour, phrases, register stability
    // ------------------------------------------------------------------
    internal static (RecordingDetail Detail, Register Headline) AnalyzeRegister(
        IReadOnlyList<(double T, double Hz)> contour,
        IReadOnlyList<(double Start, double End)> spans,
        double duration,
        double floor)
    {
        var frames = new Frames();
        var voiced = new List<(double T, double Hz)>();
        foreach (var (t, hz) in contour)
        {
            frames.T.Add(Round(t, 3));
            frames.Hz.Add(hz > 0 ? Round(hz, 1) : null);
            if (hz > 0) voiced.Add((t, hz));
        }

        var phrases = new List<Phrase>();
        // [sub_count, total] per third of a phrase: onset, mid, offset
        var sub = new int[3];
        var total = new int[3];
        foreach (var (t0, t1) in spans)
        {
            var pv = voiced.Where(v => t0 <= v.T && v.T <= t1).ToList();
            if (pv.Count == 0) continue;
            var dur = t1 - t0;
            if (dur == 0) dur = 1e-9;

            var onsetWin = pv.Where(v => v.T - t0 <= 0.08).Select(v => v.Hz).ToList();
            if (onsetWin.Count == 0) onsetWin.Add(pv[0].Hz);
            var offsetWin = pv.Where(v => t1 - v.T <= 0.12).Select(v => v.Hz).ToList();
            if (offsetWin.Count == 0) offsetWin.Add(pv[^1].Hz);
            var onsetHz = onsetWin.Average();
            var offsetHz = offsetWin.Average();
            var hzs = pv.Select(v => v.Hz).ToList();

            phrases.Add(new Phrase
            {
                Start = Round(t0, 3),
                End = Round(t1, 3),
                OnsetHz = Round(onsetHz, 1),
                OffsetHz = Round(offsetHz, 1),
                MinHz = Round(hzs.Min(), 1),
                StartedInRegister = onsetHz >= floor,
                EndedInRegister = offsetHz >= floor,
                SubRegisterPct = Round(100.0 * hzs.Count(h => h < floor) / hzs.Count, 1),
            });

            foreach (var (t, h) in pv)
            {
                var rel = (t - t0) / dur;
                var b = rel < 1.0 / 3 ? 0 : rel < 2.0 / 3 ? 1 : 2;
                total[b]++;
                if (h < floor) sub[b]++;
            }
        }

        double? Pct(int b) => total[b] > 0 ? Round(100.0 * sub[b] / total[b], 1) : null;

        var allHz = voiced.Select(v => v.Hz).ToList();
        var inReg = allHz.Where(h => h >= floor).ToList();
        var landed = phrases.Count(p => p.EndedInRegister);

        var headline = new Register
        {
            FloorHz = floor,
            InRegisterPct = allHz.Count > 0 ? Round(100.0 * inReg.Count / allHz.Count, 1) : null,
            SemitonesSd = Clean(SampleSd(allHz.Select(HzToSt).ToList())),
            InRegisterSemitonesSd = Clean(SampleSd(inReg.Select(HzToSt).ToList())),
            OnsetSubPct = Pct(0),
            MidSubPct = Pct(1),
            OffsetSubPct = Pct(2),
            PhrasesLandedPct = phrases.Count > 0 ? Math.Round(100.0 * landed / phrases.Count, MidpointRounding.ToEven) : null,
            NPhrases = phrases.Count,
        };
        var detail = new RecordingDetail
        {
            RegisterFloorHz = floor,
            SemitoneRefHz = SemitoneRefHz,
            DurationS = Round(duration, 2),
            TimeStep = 0.01,
            Frames = frames,
            Phrases = phrases,
            Summary = headline,
        };
        return (detail, headline);
    }
}
