"""Where the time goes during the phone runs: every PERF probe summed per run (ms per 28 s recording),
then the median over each build's runs, grouped into the analysis, the live graphs' data, chart drawing and
the rest of the UI.

Usage: py -I probes.py <dir> [labelA labelB]
Only the recording is counted (probes from the record tap until the Stop tap); the analysis after Stop is
reported separately. Probes nest (live.update contains live.formants, …): the groups use the outermost ones.
"""
import re
import statistics
import sys
from pathlib import Path

# Outermost probes, by group. live.update(-quick) and live.compute run on the live worker; the others on the UI thread.
GROUPS = {
    'analysis (live worker)': ['live.update', 'live.update-quick'],
    'graph data (live.compute, live worker)': ['live.compute'],
    'graph update (live.apply, UI)': ['live.apply'],
    'chart drawing (render.*Chart, UI)': ['render.TimelineChart', 'render.ContourChart', 'render.LineChart'],
    'other drawing (render.* bars, pills, UI)': ['render.ZoneBar', 'render.ZonePill', 'render.MetricScale', 'render.WaveformView'],
    'live take cards (live.takeview, UI)': ['live.takeview'],
}
DETAIL = ['live.formants', 'live.formants.path', 'live.hnr', 'live.pitch', 'live.pitchpath', 'live.hnrpath', 'live.intensity',
          'live.pulses', 'live.derived', 'live.series', 'live.assemble', 'live.postprocess', 'live.contour']
STOP = ['stop.final-live', 'stop.analyze', 'stop.save', 'analysis.measure', 'analysis.formants', 'analysis.formants.resample',
        'analysis.formants.frames', 'analysis.hnr', 'analysis.pitch', 'analysis.series']


def runs(folder):
    out = {}
    for log in sorted(folder.glob('*.logcat')):
        label = log.stem
        recording, stop = {}, {}
        for line in log.read_text(errors='replace').splitlines():
            m = re.search(r'PERF (\S+) ([\d.]+)', line)
            if not m:
                continue
            name, v = m.group(1), float(m.group(2))
            if name.startswith('alloc.'):
                continue
            # Stop and the analysis after it: everything from the first stop.* probe on.
            target = stop if (name.startswith('stop.') or name.startswith('analysis.') or stop) else recording
            target[name] = target.get(name, 0) + v
            if name.startswith('stop.'):
                stop.setdefault('_seen', 1)
        out[label] = (recording, stop)
    return out


def main():
    folder = Path(sys.argv[1])
    data = runs(folder)
    builds = sorted({l.rsplit('-', 1)[0] for l in data})
    if len(sys.argv) >= 4:
        builds = sys.argv[2:4]
    med = lambda b, part, name: statistics.median([data[l][part].get(name, 0) for l in data if l.rsplit('-', 1)[0] == b])
    print(f"{'while recording (ms per 28 s take)':44s}" + ''.join(f"{b:>12s}" for b in builds))
    for group, names in GROUPS.items():
        print(f"{group:44s}" + ''.join(f"{sum(med(b, 0, n) for n in names):12.0f}" for b in builds))
    print(f"\n{'analysis detail':44s}" + ''.join(f"{b:>12s}" for b in builds))
    for n in DETAIL:
        print(f"  {n:42s}" + ''.join(f"{med(b, 0, n):12.0f}" for b in builds))
    print(f"\n{'chart drawing detail':44s}")
    for n in GROUPS['chart drawing (render.*Chart, UI)'] + GROUPS['other drawing (render.* bars, pills, UI)']:
        print(f"  {n:42s}" + ''.join(f"{med(b, 0, n):12.0f}" for b in builds))
    print(f"\n{'after Stop (ms)':44s}")
    for n in STOP:
        print(f"  {n:42s}" + ''.join(f"{med(b, 1, n):12.0f}" for b in builds))


main()
