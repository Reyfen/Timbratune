"""Frame times and probes of phone runs (run.sh / ab.sh output), per run and per build.

Usage: py -I analyze.py <dir> [labelA labelB]
Each run <label>-<n> has <label>-<n>.latency (dumpsys SurfaceFlinger --latency samples) and
<label>-<n>.logcat (PERF probes). Per run: frames presented while recording, fps over that span,
% of frame intervals over 25 ms ("janky", HANDOFF §8d; at a 30 fps glide every frame is over 25 ms,
so use '>40ms%' then), the count over 50 ms, Stop to saved,
the live update medians and the UI stalls. Then the medians per build and, for two builds, a
two-sided shuffle (permutation) test of the janky share and fps.
"""
import random
import re
import statistics
import sys
from pathlib import Path

PENDING = 9223372036854775807


def frames(path):
    present = set()
    for line in Path(path).read_text(errors='replace').splitlines():
        parts = line.split()
        if len(parts) != 3:
            continue
        try:
            _, actual, _ = map(int, parts)
        except ValueError:
            continue
        if actual in (0, PENDING):
            continue
        present.add(actual)
    return sorted(present)


def probes(path):
    values = {}
    if not Path(path).exists():
        return values
    for line in Path(path).read_text(errors='replace').splitlines():
        m = re.search(r'PERF (\S+) ([\d.]+)', line)
        if m:
            values.setdefault(m.group(1), []).append(float(m.group(2)))
    return values


def run(folder, label):
    f = frames(folder / f'{label}.latency')
    if len(f) < 2:
        return None
    gaps = [(b - a) / 1e6 for a, b in zip(f, f[1:])]
    span = (f[-1] - f[0]) / 1e9
    p = probes(folder / f'{label}.logcat')
    stop = sum(p.get(k, [0])[-1] for k in ('stop.final-live', 'stop.analyze', 'stop.save'))
    upd = p.get('live.update', []) + p.get('live.update-quick', [])
    med = lambda k: statistics.median(p[k]) if p.get(k) else 0.0
    return {
        'run': label,
        'frames': len(f),
        'fps': len(f) / span,
        'janky%': 100 * sum(g > 25 for g in gaps) / len(gaps),
        '>50ms': sum(g > 50 for g in gaps),
        # With the glide at 30 fps (two 60 Hz frames), a late frame is one more than 40 ms after the last.
        '>40ms%': 100 * sum(g > 40 for g in gaps) / len(gaps),
        'stop s': stop / 1000,
        'analyze s': p.get('stop.analyze', [0])[-1] / 1000,
        'updates': len(upd),
        'upd med ms': statistics.median(upd) if upd else 0.0,
        'full med ms': med('live.update'),
        'formants ms': med('live.formants'),
        'path ms': med('live.formants.path'),
        'stalls': len(p.get('ui.stall', [])),
    }


def show(rows):
    keys = list(rows[0].keys())
    print(' | '.join(keys))
    for r in rows:
        print(' | '.join(f'{r[k]:.2f}' if isinstance(r[k], float) else str(r[k]) for k in keys))


def shuffle_p(a, b, n=20000):
    observed = abs(statistics.median(a) - statistics.median(b))
    pooled = a + b
    hits = 0
    rng = random.Random(1)
    for _ in range(n):
        rng.shuffle(pooled)
        x, y = pooled[:len(a)], pooled[len(a):]
        if abs(statistics.median(x) - statistics.median(y)) >= observed:
            hits += 1
    return hits / n


def main():
    folder = Path(sys.argv[1])
    labels = sorted({p.stem for p in folder.glob('*.latency')},
                    key=lambda s: (s.rsplit('-', 1)[0], int(s.rsplit('-', 1)[1]) if s.rsplit('-', 1)[-1].isdigit() else 0))
    rows = [r for r in (run(folder, l) for l in labels) if r]
    if not rows:
        print('no runs')
        return
    show(rows)
    builds = {}
    for r in rows:
        builds.setdefault(r['run'].rsplit('-', 1)[0], []).append(r)
    print()
    medians = []
    for name, rs in builds.items():
        m = {'run': f'{name} (median of {len(rs)})'}
        for k in rows[0]:
            if k != 'run':
                m[k] = float(statistics.median(r[k] for r in rs))
        medians.append(m)
    show(medians)
    pair = sys.argv[2:4] if len(sys.argv) >= 4 else list(builds)[:2]
    if len(pair) == 2 and all(p in builds for p in pair):
        for k in ('janky%', '>40ms%', 'fps', 'stop s', 'upd med ms'):
            a = [r[k] for r in builds[pair[0]]]
            b = [r[k] for r in builds[pair[1]]]
            print(f'{k}: {pair[0]} {statistics.median(a):.2f} vs {pair[1]} {statistics.median(b):.2f}, shuffle p = {shuffle_p(a, b):.3f}')


if __name__ == '__main__':
    main()
