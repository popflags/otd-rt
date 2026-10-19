#!/usr/bin/env python3
"""dips.py CSV [--fall 250] [--rebound 100]: config-independent in-stroke dips (how tools/Tune's labels were found).
Zigzag on pressure samples inside each touch: a fall >= --fall from a local peak, then a rebound >= --rebound before the lift.
Prints the dip's peak / trough (engine clock), ms after the contact, the fastest fall over 10 ms of samples (raw/ms) and the rebound.
One-sample glitches (>= 2500 jumps that come back) are skipped. 2026-10-19: slow drag dips <= ~14 raw/ms, re-clicks >= ~29 raw/ms
(but drag-into-tap dips at ~27 raw/ms exist, so speed alone does not separate them)."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load  # noqa: E402

args = sys.argv[1:]
F = float(args[args.index('--fall') + 1]) if '--fall' in args else 250
R = float(args[args.index('--rebound') + 1]) if '--rebound' in args else 100
d = load(args[0])
t, p = [], []
for k, (e, v) in enumerate(zip(d['eng'], d['p'])):
    if k == 0 or v != d['p'][k - 1]:
        t.append(e); p.append(v)
k = 1
while k < len(p) - 2:
    if p[k - 1] > 2 and abs(p[k] - p[k - 1]) >= 2500 and abs(p[k + 1] - p[k - 1]) < 2500 and p[k + 1] > 2:
        del p[k]; del t[k]
    else:
        k += 1


def fastest(a, b):
    best = 0.0
    for i in range(a, b):
        for j in range(i + 1, b + 1):
            if t[j] - t[i] >= 9.9:
                best = max(best, (p[i] - p[j]) / (t[j] - t[i]))
                break
    return best


n, i = len(p), 0
while i < n:
    if p[i] <= 2:
        i += 1
        continue
    j = i
    while j < n and p[j] > 2:
        j += 1
    hi, lo, down, k = i, None, False, i
    while k < j:
        if not down:
            if p[k] > p[hi]:
                hi = k
            if p[hi] - p[k] >= F:
                down, lo = True, k
        else:
            if p[k] < p[lo]:
                lo = k
            if p[k] - p[lo] >= R:
                reb = max(p[lo:j]) - p[lo]
                print('peak %5d @ %9.0f (+%4.0f ms after contact) -> trough %5d @ %9.0f  fall %5d  fastest %5.1f raw/ms  rebound %5d  lift +%5.0f' % (
                    p[hi], t[hi], t[hi] - t[i], p[lo], t[lo], p[hi] - p[lo], fastest(hi, lo), reb, (t[j] if j < n else t[-1]) - t[lo]))
                down, hi = False, k
        k += 1
    i = j
