#!/usr/bin/env python3
"""margin.py CSV BASEARGS T0_S LEN_MS "label|--set Name=V ..." ...: the fall excess one touch builds (the touch starting at T0_S
seconds of engine time, within its first LEN_MS), by bisection on Fast Release Distance (the smallest value at which it no longer
FastReleases there), and the margin 35 / that (above 1 = it holds with Fast Release Distance 35). Used 2026-10-20 on the CP923
mid-hold wobble (td2csv of tablet-data_1792518028, 24.79 s): live 0.89, Hold Time 300 1.20, Tap Allowance 0.4 1.18."""
import bisect
import csv
import os
import subprocess
import sys
csvf, base, t0, span = sys.argv[1], open(sys.argv[2]).read().split(), float(sys.argv[3]), float(sys.argv[4])
rows = list(csv.DictReader(open(csvf)))
e0 = float(rows[0]['engine_ms'])
def releases(extra):
    out = subprocess.run([os.path.expanduser('~/.cache/otd-rt-tools/dotnet/dotnet'), 'tools/RtTool/bin/Release/net8.0/rt.dll', 'replay', csvf] + base + extra + ['--events'], capture_output=True, text=True).stdout
    for line in out.splitlines():
        s = line.split()
        if len(s) > 3 and s[1] == 'ms' and s[2] == 'FastRelease':
            t = float(s[0]) / 1000
            if t0 - 0.005 <= t <= t0 + span / 1000:
                return True
    return False
for v in sys.argv[5:]:
    label, sets = v.split('|')
    lo, hi = 1.0, 400.0
    for _ in range(9):
        mid = (lo * hi) ** 0.5
        if releases(sets.split() + ['--set', 'FastReleaseDistance=%g' % mid]): lo = mid
        else: hi = mid
    print('%-24s fall excess needed to hold this drag: %5.1f  -> margin with FRD 35: %.2f' % (label, hi, 35 / hi))
