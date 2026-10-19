#!/usr/bin/env python3
"""phantoms.py TD_FULL.csv: lone 8191 readings (touches of <= 2 ms at 8191) with the hover distance at that moment, ms since the
previous lift and the hover 40 ms later, plus what follows. Needs the hd column (scratch/td_full.py output). Real taps bottom out at
hover ~24-26; the firmware's phantoms come 10-40 ms after a lift with the pen rising (hd 30-35 -> 90-140), or on the way down."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches  # noqa: E402

d = load(sys.argv[1])
if 'hd' not in d:
    sys.exit('needs scratch/td_full.py output (hover distance column)')
P, E, HD = d['p'], d['eng'], d['hd']
last_end = None
tl = touches(P)
for idx, (a, b) in enumerate(tl):
    if P[a] >= 8191 and E[b - 1] - E[a] <= 2:
        hd_at = [h for h in HD[a:b] if h != 255]
        after = [h for h in HD[b:b + 40] if h != 255]
        nxt = tl[idx + 1][0] if idx + 1 < len(tl) else None
        print('%8.2f s: hover %s, %s ms after the previous lift, hover 40 ms later %s, next touch %s ms later' % (
            E[a] / 1000, min(hd_at) if hd_at else '?', '%.0f' % (E[a] - last_end) if last_end is not None else '?',
            after[-1] if after else '?', '%.0f' % (E[nxt] - E[a]) if nxt else '-'))
    else:
        last_end = E[b - 1]
