#!/usr/bin/env python3
"""patterns.py DIAGNOSTICS.csv [--gate MS,...] [--split MS]: tap / drag sequences and what a Touchdown Gate would do to them.

Touches that pressed (live `event` column) are split into taps (< 250 ms on the surface), drags (>= 400 ms) and in-between (M).
Prints:
  - touch-duration histogram, sequences (touches whose onsets are < --split ms apart, default 1000) by pattern (T, TD, TTD, ...);
  - onset-to-onset intervals per pattern position (T->T, T->D, D->T) with the lift-to-touch gap;
  - re-presses without lifting (Rearm) by touch kind and time after the touch;
  - for each --gate value (default 500,480,450): the Touchdown Gate rule (a touchdown clicks no sooner than MS after the previous
    touchdown's click, at most one click waiting; a touch landing before it goes out joins it) applied to the live touchdowns:
    clicks held back per pair, delay, longest run of consecutive held clicks, touches merged.
Intervals use the PC clock (t_ms). Used on rt-20261020-002836.csv (2026-10-20): T->T median 528 ms, T->D 412-448, D re-press ~552 ms
after the drag's touch; gate 500 holds back 255 of 935 touchdown clicks (`rt replay ... --set TouchdownGate=500` agrees)."""
import collections
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches, q  # noqa: E402

args = sys.argv[1:]
gates = [float(g) for g in args[args.index('--gate') + 1].split(',')] if '--gate' in args else [500, 480, 450]
split = float(args[args.index('--split') + 1]) if '--split' in args else 1000
d = load(args[0])
if 'event' not in d:
    sys.exit('needs a diagnostics CSV (pressed/event columns)')
P, H, EV = d['p'], d['host'], d['event']

acts = []
for a, b in touches(P):
    press = [k for k in range(a, b) if EV[k] in (1, 2)]
    if not press:
        continue
    dur = H[b - 1] - H[a]
    acts.append(dict(on=H[a], end=H[b - 1], dur=dur, kind='T' if dur < 250 else 'D' if dur >= 400 else 'M',
                     contact=next((H[k] for k in press if EV[k] == 1), None),
                     rearm=[H[k] - H[a] for k in press if EV[k] == 2]))
print('%d pressed touches: %s' % (len(acts), dict(collections.Counter(x['kind'] for x in acts))))
hist = collections.Counter(next(e for e in (10, 20, 40, 60, 80, 100, 150, 200, 250, 300, 400, 500, 700, 1000, 2000, 1e9) if x['dur'] < e) for x in acts)
print('touch duration: ' + ', '.join('<%g ms %d' % (e, hist[e]) for e in sorted(hist)))

seqs = [[acts[0]]]
for x in acts[1:]:
    if x['on'] - seqs[-1][-1]['on'] < split:
        seqs[-1].append(x)
    else:
        seqs.append([x])
pat = collections.Counter(''.join(x['kind'] for x in s) for s in seqs)
print('\n%d sequences (onsets < %.0f ms apart), most common:' % (len(seqs), split))
for k, v in pat.most_common(15):
    print('  %-12s %d' % (k if len(k) <= 12 else k[:9] + '...', v))

print('\nintervals by pattern (median onset-to-onset, p10-p90, median lift-to-touch):')
for p in ('TT', 'TD', 'DT', 'TTD', 'TTT', 'TDT', 'DTT', 'TTTD'):
    S = [s for s in seqs if ''.join(x['kind'] for x in s) == p]
    if not S:
        continue
    cols = []
    for i in range(1, len(p)):
        iv = [s[i]['on'] - s[i - 1]['on'] for s in S]
        gap = [s[i]['on'] - s[i - 1]['end'] for s in S]
        cols.append('%s->%s %3.0f (%3.0f-%3.0f) lift-touch %3.0f' % (p[i - 1], p[i], q(iv, .5), q(iv, .1), q(iv, .9), q(gap, .5)))
    print('  %-5s n=%3d  %s' % (p, len(S), ' | '.join(cols)))
pairs = collections.defaultdict(list)
for i in range(1, len(acts)):
    if acts[i]['on'] - acts[i - 1]['on'] < 1200:
        pairs[acts[i - 1]['kind'] + acts[i]['kind']].append(acts[i]['on'] - acts[i - 1]['on'])
print('all consecutive pairs < 1200 ms:')
for k in ('TT', 'TD', 'DT', 'DD'):
    v = pairs.get(k, [])
    if v:
        print('  %s n=%3d onset-to-onset p10/50/90 %3.0f / %3.0f / %3.0f, under 500: %d' % (k, len(v), q(v, .1), q(v, .5), q(v, .9), sum(1 for x in v if x < 500)))
print('re-presses without lifting, ms after the touch:')
for k in 'TDM':
    v = [r for x in acts if x['kind'] == k for r in x['rearm']]
    if v:
        print('  in %s: %d, p10/50/90 %.0f / %.0f / %.0f' % (k, len(v), q(v, .1), q(v, .5), q(v, .9)))

for gate in gates:
    last, wait, run, longest, merged = -1e18, None, 0, 0, 0
    delays = collections.defaultdict(list)
    prev = None
    for x in acts:
        c = x['contact']
        if c is None:
            continue
        if wait is not None and c < wait:
            merged += 1
            prev = x
            continue
        due = last + gate
        out = max(c, due)
        wait = out if out > c else None
        last = out
        key = (prev['kind'] if prev and x['on'] - prev['on'] < 1200 else '-') + x['kind']
        delays[key].append(out - c)
        run = run + 1 if out > c else 0
        longest = max(longest, run)
        prev = x
    held = [v for vs in delays.values() for v in vs if v > 0]
    total = sum(len(v) for v in delays.values())
    print('\ngate %.0f ms: %d of %d touchdown clicks held back, delay p50/p90/max %.0f / %.0f / %.0f ms, longest run %d, touches merged %d' % (
        gate, len(held), total, q(held, .5), q(held, .9), max(held) if held else 0, longest, merged))
    for k in ('TT', 'TD', 'DT', 'DD', '-T', '-D'):
        v = delays.get(k, [])
        h = [x for x in v if x > 0]
        if v:
            print('  %s n=%3d held %3d, delay p50/p90 %3.0f / %3.0f' % (k, len(v), len(h), q(h, .5) if h else 0, q(h, .9) if h else 0))
