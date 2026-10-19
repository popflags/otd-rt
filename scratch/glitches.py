#!/usr/bin/env python3
"""glitches.py CSV [--min 2500]: one- or two-sample pressure excursions while the pen is down that come straight back (firmware
glitches: 8191 -> 692 -> 8191, 8117 -> 0 -> 8113 on 83a52e3c; 0 expected on a12b3929), plus the largest genuine per-sample fall / rise
(genuine changes stayed <= ~1800 down and ~520 up after the contact on all 2026-10-19 logs; GlitchJump 2500 sits between)."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load  # noqa: E402

args = sys.argv[1:]
M = float(args[args.index('--min') + 1]) if '--min' in args else 2500
d = load(args[0])
t, p = [], []
for k, (e, v) in enumerate(zip(d['eng'], d['p'])):
    if k == 0 or v != d['p'][k - 1]:
        t.append(e); p.append(v)
found, falls, rises = [], [], []
k = 2
while k < len(p) - 3:
    if p[k - 1] > 2 and p[k - 2] > 2:
        hit = None
        for L in (1, 2):
            if all(abs(p[k + q] - p[k - 1]) >= M for q in range(L)) and abs(p[k + L] - p[k - 1]) < M and p[k + L] > 2:
                hit = L
                break
        if hit:
            found.append((t[k], p[k - 1], p[k:k + hit], p[k + hit]))
            k += hit + 1
            continue
        dlt = p[k] - p[k - 1]
        (falls if dlt < 0 else rises).append(abs(dlt))
    k += 1
print('%d glitches (>= %d, 1-2 samples, back within %d); largest genuine fall %d, rise %d per sample' % (
    len(found), M, M, max(falls or [0]), max(rises or [0])))
for e, a, g, b in found[:40]:
    print('   %9.0f ms (engine): %d -> %s -> %d' % (e, a, g, b))
