#!/usr/bin/env python3
"""pertap.py CSV BASEARGS "label|--set Name=V ..." ...: per touch, how much later (or earlier) its first release comes with each
variant than with the settings in BASEARGS (a file of --set lines, e.g. scratch/owner-settings.args): taps (touches < 250 ms) and
drags separately, mean shift, how many moved and the largest. DETAIL=1 lists the drags that moved. Used 2026-10-20 to price CP923
setting changes (Hold Time 300: taps +0.34 ms on average)."""
import bisect
import csv
import os
import subprocess
import sys
csvf, base = sys.argv[1], open(sys.argv[2]).read().split()
rows = list(csv.DictReader(open(csvf)))
p = [int(r['raw']) for r in rows]; h = [float(r['t_ms']) for r in rows]; e = [float(r['engine_ms']) for r in rows]
touch = []; i = 0
while i < len(p):
    if p[i] > 2:
        j = i
        while j < len(p) and p[j] > 2: j += 1
        touch.append((e[i], e[j - 1], h[j - 1] - h[i])); i = j
    else: i += 1
def first_release(extra):
    out = subprocess.run([os.path.expanduser('~/.cache/otd-rt-tools/dotnet/dotnet'), 'tools/RtTool/bin/Release/net8.0/rt.dll', 'replay', csvf] + base + extra + ['--events'], capture_output=True, text=True).stdout
    rel = []
    for line in out.splitlines():
        s = line.split()
        if len(s) > 3 and s[1] == 'ms' and s[2] in ('FastRelease', 'Release', 'Lift'):
            rel.append(float(s[0]) + e[0])
    res = {}

    for k, (a, b, d) in enumerate(touch):
        x = bisect.bisect_left(rel, a)
        if x < len(rel) and rel[x] <= b + 0.5: res[k] = rel[x] - a
    return res
b0 = first_release([])
for v in sys.argv[3:]:
    label, sets = v.split('|')
    r = first_release(sets.split())
    for kind, cond in (('taps', lambda d: d < 250), ('drags', lambda d: d >= 250)):
        diffs = [r[k] - b0[k] for k in b0 if k in r and cond(touch[k][2])]
        later = sum(1 for x in diffs if x > 0.05); earlier = sum(1 for x in diffs if x < -0.05)
        print('%-26s %-5s n=%3d  mean %+5.2f ms  later %2d (max %+5.1f)  earlier %2d' % (label, kind, len(diffs), sum(diffs) / max(1, len(diffs)), later, max(diffs, default=0), earlier))
        if kind == "drags" and os.environ.get("DETAIL"):
            for k in b0:
                if k in r and cond(touch[k][2]) and abs(r[k] - b0[k]) > 0.05: print("      touch at %.2f s, %4.0f ms long: released %.0f -> %.0f ms after the touch" % ((touch[k][0] - e[0]) / 1000, touch[k][2], b0[k], r[k]))
