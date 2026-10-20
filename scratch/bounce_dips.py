#!/usr/bin/env python3
"""bounce_dips.py CSV... [--min 50]: one-report pressure drops of --min or more that come straight back (the next report recovers at
least 60 % of the drop), away from side-button changes and lifts, grouped by the level they start from. On the CP923 (firmware
9f4a1b39 / e9a2e0a9, 2026-10-20) all 28 sat at 767-805 raw, on rising presses, 51-231 deep: a firmware artefact near 800 that
releases landings (2 of the 3 cut-outs in the first CP923 gameplay session). Re-run on new firmware to see whether it is gone."""
import csv
import sys
from collections import Counter

args = sys.argv[1:]
lim = float(args[args.index('--min') + 1]) if '--min' in args else 50
files = [a for k, a in enumerate(args) if not a.startswith('--') and (k == 0 or args[k - 1] != '--min')]
for f in files:
    rows = list(csv.DictReader(open(f)))
    p = [int(r['raw']) for r in rows]
    b = [int(r.get('buttons', 0) or 0) for r in rows]
    dips = []
    for i in range(12, len(p) - 12):
        if min(p[i - 3:i + 4]) <= 2 or any(b[j] != b[j - 1] for j in range(i - 10, i + 11)):
            continue
        d, back = p[i] - p[i - 1], p[i + 1] - p[i]
        if d <= -lim and back >= 0.6 * -d:
            dips.append((p[i - 1], d, back, p[i - 1] - p[i - 2]))
    print('%s: %d one-report dips >= %g that come back; by level: %s'
          % (f.split('/')[-1], len(dips), lim, sorted(Counter(x[0] // 100 * 100 for x in dips).items())))
    for x in dips[:15]:
        print('    from %5d: %+5d, then %+5d (step before %+d)' % x)
