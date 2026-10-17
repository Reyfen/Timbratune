"""CPU time of the app's threads while recording (run.sh's .threads1/.threads2 snapshots), per run and the
median per build or variant, grouped: the UI thread, the render threads, the live analysis (its own thread
and the .NET thread pool, where its parallel parts run), the garbage collector, the rest. Also the whole
device's busy share (.cpu1/.cpu2) and the frame times (analyze.py's numbers).

Usage: py -I threads.py <dir>
CPU is in seconds over the recording window (about 28 s), and as % of one core.
"""
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
TICK = 100.0  # clock ticks per second (getconf CLK_TCK on the phones)


def group(tid, pid, name):
    if tid == pid:
        return 'UI thread'
    if name in ('Render Thread', 'RenderThread') or name.startswith('hwuiTask'):
        return 'render threads'
    if name.startswith('Live analysis'):
        return 'live analysis thread'
    if name.startswith('.NET TP Worker') or name.startswith('.NET Long Runni'):
        return 'thread pool (analysis parallel parts, tasks)'
    if name.startswith('SGen') or name in ('Finalizer', 'HeapTaskDaemon', 'FinalizerDaemon', 'ReferenceQueueD'):
        return 'garbage collection'
    return 'other'


GROUPS = ['UI thread', 'render threads', 'live analysis thread', 'thread pool (analysis parallel parts, tasks)', 'garbage collection', 'other']


def snapshot(p):
    pid, rows = None, {}
    for line in Path(p).read_text().splitlines():
        q = line.split()
        if not q:
            continue
        if q[0] == 'pid':
            pid = q[1]
            continue
        if q[0] == 'process':
            rows['process'] = (int(q[1]) + int(q[2]), 'process')
            continue
        if len(q) >= 4:
            rows[q[0]] = (int(q[1]) + int(q[2]), ' '.join(q[3:]))
    return pid, rows


def cpu(p):
    q = Path(p).read_text().split()
    v = [int(x) for x in q[1:8]]
    return sum(v), v[3] + v[4]  # total, idle + iowait


def run(folder, label):
    p1, p2 = folder / f'{label}.threads1', folder / f'{label}.threads2'
    if not (p1.exists() and p2.exists()):
        return None
    pid, a = snapshot(p1)
    _, b = snapshot(p2)
    out = {g: 0.0 for g in GROUPS}
    for tid, (ticks, name) in b.items():
        if tid == 'process':
            continue
        before = a.get(tid, (0, name))[0]
        out[group(tid, pid, name)] += (ticks - before) / TICK
    # Threads that ended in between (thread-pool workers, the analysis' parallel parts) only show in the
    # process total: what it has beyond the threads still alive is theirs.
    if 'process' in a and 'process' in b:
        alive = sum(out.values())
        out['thread pool (analysis parallel parts, tasks)'] += max(0.0, (b['process'][0] - a['process'][0]) / TICK - alive)
    t1, i1 = cpu(folder / f'{label}.cpu1')
    t2, i2 = cpu(folder / f'{label}.cpu2')
    out['device busy %'] = 100 * (1 - (i2 - i1) / max(1, t2 - t1))
    out['window s'] = (t2 - t1) / TICK / 8  # 8 cores
    return out


def main():
    folder = Path(sys.argv[1])
    labels = sorted({p.stem for p in folder.glob('*.threads2')})
    variants = {}
    for l in labels:
        r = run(folder, l)
        if r:
            variants.setdefault(l.rsplit('-', 1)[0], []).append(r)
    import analyze  # frame times per run
    names = list(variants)
    print(f"{'CPU seconds while recording (median)':46s}" + ''.join(f"{n:>11s}" for n in names))
    for g in GROUPS:
        print(f"{g:46s}" + ''.join(f"{statistics.median(r[g] for r in variants[n]):11.2f}" for n in names))
    total = {n: statistics.median(sum(r[g] for g in GROUPS) for r in variants[n]) for n in names}
    print(f"{'app total':46s}" + ''.join(f"{total[n]:11.2f}" for n in names))
    print(f"{'device busy % (8 cores)':46s}" + ''.join(f"{statistics.median(r['device busy %'] for r in variants[n]):11.1f}" for n in names))
    print()
    frames = {}
    for l in labels:
        r = analyze.run(folder, l)
        if r:
            frames.setdefault(l.rsplit('-', 1)[0], []).append(r)
    for k in ('fps', 'janky%', '>40ms%', '>50ms', 'updates', 'upd med ms', 'stalls'):
        print(f"{k:46s}" + ''.join(f"{statistics.median(r[k] for r in frames.get(n, [{k: float('nan')}])):11.2f}" for n in names))


if __name__ == '__main__':
    main()
