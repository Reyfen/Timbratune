"""Scores a formant-check dump directory against the benches HIGH-PITCH-PLAN.md uses (dev tool; no change to the app).

    python -I tools/ceiling-bench/score.py <dump dir> [<dump dir> ...] [--data <Voice-Analyzer-Dataset/data>] [--brief]

--brief prints one line per dump dir with the acceptance numbers of HIGH-PITCH-PLAN.md §4 (for sweeps).

Each dump dir holds tools/formant-check -dump output (one <rec_id>.txt per file: m cards, c path segments, s/p graph
formants, z size, r vowel-core rows, w weight frames). Which benches are scored depends on which rec_ids are present:

  owner_*      the owner's takes: cards, dominant ceiling, share of vowel rows at 4500 Hz, size.
  ears_*       EARS (Voice-Analyzer-Dataset): per speaker, passage 01 read in a high pitch minus the regular reading
               (VTL and F3; women and men), as a distribution, not only a median: median, p10, p90, share within
               +-0.5 cm, share beyond +1 cm; the share of the high-pitch take's vowel rows at 4500 Hz; the take-to-take
               ICC(1) of VTL over the three regular passages.
  vtr_*        VTR-Formants: F2 and F3 RMSE and gross-error share (> 15 %) against the hand-corrected tracks on the
               app's vowel-core rows (truth shifted by -10 ms, as the research's vtr2.py), women and men.
  tmasc_*      OSF trans/masc: r between our VTL and the authors' sentence VTL (Johnson 2020).

Acceptance lines (HIGH-PITCH-PLAN.md): the research's medians must hold AND the tails: EARS women high-pitch VTL
change p90 <= +0.5 cm and share beyond +1 cm <= 5 %; VTR F3 RMSE women <= 195 Hz, men <= 160 Hz; tmasc r >= 0.75.
"""
import argparse
import contextlib
import csv
import io
import glob
import os
import statistics as st
from collections import Counter, defaultdict

DEFAULT_DATA = r'C:\Projects\Reyfen\Voice-Analyzer-Dataset\data'
BRIEF = {}  # the acceptance numbers of the dump dir being scored, for --brief
K = (2.5, 3.5)


def load(path):
    d = {'m': None, 'z': None, 'c': [], 's': [], 'r': [], 'w': []}
    for line in open(path):
        p = line.split()
        if not p:
            continue
        tag = p[0]
        if tag not in d:
            continue
        vals = [float(x) if x not in ('NaN', '') else float('nan') for x in p[1:]]
        if tag in ('m', 'z'):
            d[tag] = vals
        elif tag in d:
            d[tag].append(vals)
    return d


def ceiling_at(segs, t):
    c = segs[0][1] if segs else float('nan')
    for s, cc in segs:
        if s <= t:
            c = cc
    return c


def share_at(d, hz=4500):
    rows = d['r']
    if not rows:
        return float('nan')
    return sum(ceiling_at(d['c'], r[0]) == hz for r in rows) / len(rows)


def dominant(d):
    rows = d['r']
    if not rows:
        return float('nan')
    return Counter(ceiling_at(d['c'], r[0]) for r in rows).most_common(1)[0][0]


def vtl(d):
    return d['z'][1] if d['z'] else float('nan')


def q(xs, p):
    xs = sorted(x for x in xs if x == x)
    if not xs:
        return float('nan')
    return xs[min(len(xs) - 1, int(p * len(xs)))]


def icc(groups):
    groups = [g for g in groups if len(g) >= 2]
    if len(groups) < 2:
        return float('nan')
    k = st.mean(len(g) for g in groups)
    allv = [x for g in groups for x in g]
    gm = st.mean(allv)
    msb = sum(len(g) * (st.mean(g) - gm) ** 2 for g in groups) / (len(groups) - 1)
    msw = sum((x - st.mean(g)) ** 2 for g in groups for x in g) / (len(allv) - len(groups))
    return (msb - msw) / (msb + (k - 1) * msw)


def meta(data):
    rec = {}
    with open(os.path.join(data, 'recordings.csv'), encoding='utf-8') as f:
        for row in csv.DictReader(f):
            rec[row['rec_id']] = row
    sex = {}
    with open(os.path.join(data, 'speakers.csv'), encoding='utf-8') as f:
        for row in csv.DictReader(f):
            sex[row['speaker_uid']] = {'male': 'M', 'female': 'F'}.get(row['gender'], '?')
    return rec, sex


def score_owner(dumps):
    print('-- owner takes')
    for name, d in sorted(dumps.items()):
        m = d['m'] or [float('nan')] * 4
        BRIEF[name] = f'{dominant(d):.0f} {vtl(d):.1f}'
        print(f'   {name}: F1 {m[0]:.0f} F2 {m[1]:.0f} F3 {m[2]:.0f} weight {m[3]:.2f} | dominant {dominant(d):.0f} Hz, rows at 4500 Hz {100 * share_at(d):.0f} %, '
              f'{len(d["c"]) - 1} switches | size {vtl(d):.2f} cm')


def score_ears(dumps, rec, sex):
    print('-- EARS')
    by = defaultdict(dict)
    for name, d in dumps.items():
        r = rec.get(name)
        if not r:
            continue
        by[r['speaker_uid']][(r['text_id'], r['style'])] = d
    for s in 'FM':
        rows = []
        for spk, takes in by.items():
            if sex.get(spk) != s:
                continue
            h, g = takes.get(('rainbow_01', 'highpitch')), takes.get(('rainbow_01', 'regular'))
            if h and g and h['m'] and g['m']:
                rows.append((vtl(h) - vtl(g), h['m'][2] - g['m'][2], share_at(h), share_at(g), dominant(h), h['z'][2] if h['z'] else float('nan'), spk))
        if not rows:
            continue
        d = [x[0] for x in rows]
        bad = [x for x in rows if x[0] > 1]
        if bad:
            print(f'   {"women" if s == "F" else "men"} beyond +1 cm: ' + ', '.join(f'{x[6]} {x[0]:+.1f} cm @{x[4]:.0f} Hz F0 {x[5]:.0f}' for x in sorted(bad, key=lambda x: -x[0])))
        BRIEF[s + ' high'] = f'{st.median(d):+.2f} p90 {q(d, .9):+.2f} >1cm {100 * sum(x > 1 for x in d) / len(d):.0f}%'
        print(f'   {"women" if s == "F" else "men"} high pitch minus regular, n {len(rows)}: VTL median {st.median(d):+.2f} cm, p10 {q(d, .1):+.2f}, p90 {q(d, .9):+.2f}, '
              f'within 0.5 cm {100 * sum(abs(x) <= 0.5 for x in d) / len(d):.0f} %, beyond +1 cm {100 * sum(x > 1 for x in d) / len(d):.0f} %; '
              f'F3 median {st.median(x[1] for x in rows):+.0f} Hz; rows at 4500 Hz: high {100 * st.median(x[2] for x in rows):.0f} % (mean {100 * st.mean(x[2] for x in rows):.0f} %), regular {100 * st.median(x[3] for x in rows):.0f} %')
    for s in 'FM':
        groups = []
        for spk, takes in by.items():
            if sex.get(spk) != s:
                continue
            g = [vtl(takes[k]) for k in (('rainbow_01', 'regular'), ('rainbow_02', 'regular'), ('rainbow_03', 'regular')) if k in takes]
            g = [x for x in g if x == x]
            if len(g) == 3:
                groups.append(g)
        if groups:
            BRIEF[s + ' icc'] = f'{icc(groups):.2f}'
            print(f'   {"women" if s == "F" else "men"} take-to-take ICC of VTL over the three regular passages: {icc(groups):.2f} (n {len(groups)}); regular VTL median {st.median(g[0] for g in groups):.2f} cm')
    allg = []
    for spk, takes in by.items():
        g = [vtl(takes[k]) for k in (('rainbow_01', 'regular'), ('rainbow_02', 'regular'), ('rainbow_03', 'regular')) if k in takes]
        g = [x for x in g if x == x]
        if len(g) == 3:
            allg.append(g)
    if allg:
        BRIEF['icc'] = f'{icc(allg):.2f}'
        print(f'   all speakers ICC {icc(allg):.2f} (n {len(allg)})')


def score_vtr(dumps, rec, sex, data):
    print('-- VTR (app vowel-core rows vs hand-corrected tracks shifted -10 ms)')
    truth = defaultdict(dict)
    with open(os.path.join(data, 'labels', 'formants_tracks.csv'), encoding='utf-8') as f:
        for row in csv.DictReader(f):
            if row['rec_id'] in dumps:
                truth[row['rec_id']][round(float(row['t_s']), 2)] = (float(row['F1']), float(row['F2']), float(row['F3']))
    err = {'F': defaultdict(list), 'M': defaultdict(list)}
    for name, d in dumps.items():
        r = rec.get(name)
        if not r or name not in truth:
            continue
        s = sex.get(r['speaker_uid'], '?')
        if s not in err:
            continue
        tr = truth[name]
        for row in d['r']:
            t = round(round((row[0] - 0.01) / 0.01) * 0.01, 2)
            if t not in tr:
                continue
            tf = tr[t]
            for k, col in ((2, 'F2'), (3, 'F3')):
                if tf[k - 1] > 0 and row[k] == row[k]:
                    err[s][col].append((row[k] - tf[k - 1], tf[k - 1]))
    for s in 'FM':
        parts = []
        for col in ('F2', 'F3'):
            e = err[s][col]
            if not e:
                continue
            rmse = (sum(x * x for x, _ in e) / len(e)) ** 0.5
            gross = 100 * sum(abs(x) > 0.15 * t for x, t in e) / len(e)
            parts.append(f'{col} RMSE {rmse:.0f} Hz, gross {gross:.1f} %')
            if col == 'F3':
                BRIEF[s + ' vtr'] = f'{rmse:.0f}/{gross:.1f}%'
        if parts:
            print(f'   {"women" if s == "F" else "men"} ({len(err[s]["F3"])} rows): ' + '; '.join(parts))


def score_tmasc(dumps, data):
    print('-- OSF trans/masc')
    auth = {}
    with open(os.path.join(data, 'labels', 'acoustics_reported.csv'), encoding='utf-8') as f:
        for row in csv.DictReader(f):
            if row['rec_id'] in dumps and row['measure'] == 'sentVTL':
                auth[row['rec_id']] = float(row['value'])
    pairs = [(vtl(d), auth[n]) for n, d in dumps.items() if n in auth and vtl(d) == vtl(d)]
    if len(pairs) < 3:
        return
    xs, ys = [p[0] for p in pairs], [p[1] for p in pairs]
    mx, my = st.mean(xs), st.mean(ys)
    r = sum((x - mx) * (y - my) for x, y in pairs) / (sum((x - mx) ** 2 for x in xs) * sum((y - my) ** 2 for y in ys)) ** 0.5
    BRIEF['tm'] = f'{r:.2f}'
    print(f'   our VTL vs the authors\' sentence VTL: r {r:.2f}, n {len(pairs)}; our median {st.median(xs):.2f} cm, theirs {st.median(ys):.2f} cm')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dumps', nargs='+')
    ap.add_argument('--data', default=DEFAULT_DATA)
    ap.add_argument('--brief', action='store_true')
    a = ap.parse_args()
    rec, sex = meta(a.data)
    for dump_dir in a.dumps:
        dumps = {os.path.basename(f)[:-4]: load(f) for f in glob.glob(os.path.join(dump_dir, '*.txt'))}
        BRIEF.clear()
        out = io.StringIO() if a.brief else None
        with contextlib.redirect_stdout(out) if out else contextlib.nullcontext():
            score_dir(dump_dir, dumps, rec, sex, a.data)
        if a.brief:
            b, name = BRIEF, os.path.basename(os.path.normpath(dump_dir))
            print(f'{name:10s} owner {b.get("owner_take22", "-")} / {b.get("owner_take23", "-")} | '
                  f'women high {b.get("F high", "-")} | men high {b.get("M high", "-")} | ICC {b.get("icc", "-")} '
                  f'({b.get("F icc", "-")}/{b.get("M icc", "-")}) | VTR F3 {b.get("F vtr", "-")} {b.get("M vtr", "-")} | tm r {b.get("tm", "-")}', flush=True)


def score_dir(dump_dir, dumps, rec, sex, data):
    print(f'== {dump_dir}: {len(dumps)} files')
    groups = defaultdict(dict)
    for n, d in dumps.items():
        groups[n.split('_')[0]][n] = d
    if 'owner' in groups:
        score_owner(groups['owner'])
    if 'ears' in groups:
        score_ears(groups['ears'], rec, sex)
    if 'vtr' in groups:
        score_vtr(groups['vtr'], rec, sex, data)
    if 'tmasc' in groups:
        score_tmasc(groups['tmasc'], data)


if __name__ == '__main__':
    main()
