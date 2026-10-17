"""Scores the noise bench: how far each chain's metrics land from the clean take's metrics.

usage: python score.py results.csv [chain_for_detail] [reference_chain]

Reference = <reference_chain> (default 'hp50') on <take>__clean.wav. Not 'none': the VCTK
fixtures carry room rumble below 50 Hz (-22 dB of their energy) that alone makes stray
voicing (a 96 Hz voice with frames at 488 Hz), so the unfiltered clean take is not the truth.

Each metric's error is divided by a tolerance (what a user would notice as a real change) and
capped at 5, so one wild number can't dominate; the score is the mean over metrics: 0 = same
as clean, 1 = off by about one tolerance on average. Lower is better.
"""
import csv
import sys
from collections import defaultdict

import numpy as np

# metric: (tolerance, relative?)  relative = tolerance is a fraction of the clean value
TOL = {
    'pitch_mean': (0.03, True), 'pitch_median': (0.02, True), 'in_register': (5, False),
    'f1': (0.06, True), 'f2': (0.05, True), 'f3': (0.05, True),
    'hnr': (2, False), 'jitter': (0.5, False), 'shimmer': (2, False), 'weight': (2, False),
}
CAP = 5


def load(path):
    rows = {}
    with open(path) as f:
        for r in csv.DictReader(f):
            chain, name = r['file'].split('/')
            parts = name[:-4].split('__')
            take, cond = parts[0], '__'.join(parts[1:])
            vals = {k: (float(v) if v else None) for k, v in r.items() if k != 'file'}
            rows[(chain, take, cond)] = vals
    return rows


def err(m, v, ref):
    tol, rel = TOL[m]
    if ref is None:
        return None
    if v is None:
        return CAP
    e = abs(v - ref) / (tol * abs(ref) if rel else tol)
    return min(e, CAP)


def score(vals, ref):
    es = [err(m, vals[m], ref[m]) for m in TOL]
    es = [e for e in es if e is not None]
    return float(np.mean(es))


def main():
    rows = load(sys.argv[1])
    detail = sys.argv[2] if len(sys.argv) > 2 else None
    ref_chain = sys.argv[3] if len(sys.argv) > 3 else 'hp50'
    refs = {t: v for (c, t, cond), v in rows.items() if c == ref_chain and cond == 'clean'}
    chains = sorted({c for c, _, _ in rows}, key=lambda c: (c != 'none', c))
    conds = sorted({cond for _, _, cond in rows})

    def kind(cond):
        return cond.split('__')[0]

    kinds = list(dict.fromkeys(kind(c) for c in conds))
    by = defaultdict(list)
    per_metric = defaultdict(list)
    for (c, t, cond), v in rows.items():
        s = score(v, refs[t])
        by[(c, kind(cond))].append(s)
        snr = cond.split('__snr')[1] if '__snr' in cond else None
        if snr:
            by[(c, 'snr' + snr)].append(s)
            by[(c, 'ALL noise')].append(s)
        else:
            by[(c, 'ALL channel')].append(s)
        if c == detail:
            for m in TOL:
                e = err(m, v[m], refs[t][m])
                if e is not None:
                    per_metric[(kind(cond), m)].append(e)

    def table(title, cols):
        print(f'\n{title}')
        w = max(len(c) for c in chains) + 2
        print(' ' * w + ''.join(f'{c[:9]:>10}' for c in cols))
        for ch in chains:
            line = f'{ch:<{w}}'
            for col in cols:
                xs = by.get((ch, col))
                line += f'{np.mean(xs):10.2f}' if xs else f'{"":>10}'
            print(line)

    noise_kinds = [k for k in kinds if any('__snr' in c and kind(c) == k for c in conds)]
    chan_kinds = [k for k in kinds if k not in noise_kinds]
    table('Score by noise type (mean over takes and SNRs; 0 = same as clean, lower is better)', noise_kinds + ['ALL noise'])
    table('Score by SNR (dB)', ['snr30', 'snr20', 'snr10', 'snr5', 'snr0'])
    table('Score on clean input and channel effects (should stay near 0 on clean)', chan_kinds + ['ALL channel'])
    if detail:
        print(f'\nPer-metric error / tolerance for {detail}')
        print(f'{"":12}' + ''.join(f'{m[:9]:>10}' for m in TOL))
        for k in kinds:
            print(f'{k:12}' + ''.join(f'{np.mean(per_metric[(k, m)]):10.2f}' if per_metric[(k, m)] else f'{"":>10}' for m in TOL))
    print('\nClean reference values')
    for t, v in sorted(refs.items()):
        print(f'  {t}: ' + ', '.join(f'{m} {v[m]}' for m in TOL))


if __name__ == '__main__':
    main()
