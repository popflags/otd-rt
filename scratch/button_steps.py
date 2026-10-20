#!/usr/bin/env python3
"""button_steps.py TABLET_DATA.txt: what a side-button change does to the analog pen's (CP923) pressure on the PTK-670 firmware.
Per change type while touching (status byte 2 bits 1-3: 02 = button 1, 04 = button 2), the per-report pressure step at offsets
-6..+10 from the report that carries the new bits (p10 / p50 / p90, and how many fall or rise by 80+), then where the report gaps
sit: host time over 8-report windows ending at each offset, minus 8 ms, averaged over all changes of that type (Linux debugger
Deltas are bursty, so only the average shows the gap). Findings on 1792513436 (2026-10-20): the step is on the change report; a
ramp of 85-150 per report follows for ~5 reports and an overshoot for ~4 more; button 1 presses fall ~110 per report for ~6 reports
BEFORE the bits change, after a gap 8-9 reports earlier."""
import re
import sys

rows = []
t = 0.0
for line in open(sys.argv[1], errors='replace'):
    m = re.search(r'\{ ([0-9A-Fa-f ]+) \}, Delta:([0-9.]+)ms', line)
    if not m:
        continue
    b = bytes.fromhex(m.group(1))
    t += float(m.group(2))
    if b[0] != 0x1E or len(b) < 18:
        continue
    rows.append((t, b[2] & 0x0E, b[2] & 1, b[9] | b[10] << 8))


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else float('nan')


groups = {}
for i in range(40, len(rows) - 40):
    if rows[i][1] == rows[i - 1][1]:
        continue
    touching = all(rows[j][2] for j in range(i - 8, i + 12))
    hover = not any(rows[j][2] for j in range(i - 8, i + 12))
    if touching or hover:
        groups.setdefault(('touch' if touching else 'hover', rows[i - 1][1], rows[i][1]), []).append(i)

for (where, a, b), idx in sorted(groups.items()):
    if len(idx) < 5:
        continue
    print('%s %02X -> %02X: %d changes' % (where, a, b, len(idx)))
    if where == 'touch':
        for d in range(-6, 11):
            v = [rows[i + d][3] - rows[i + d - 1][3] for i in idx]
            print('   offset %+3d: step p10 %5d p50 %5d p90 %5d   <= -80: %3d   >= 80: %3d'
                  % (d, pct(v, .1), pct(v, .5), pct(v, .9), sum(x <= -80 for x in v), sum(x >= 80 for x in v)))
    W = 8
    gaps = ['%+d:%.1f' % (d, sum(rows[i + d][0] - rows[i + d - W][0] - W for i in idx) / len(idx)) for d in range(-11, 12)]
    print('   extra host ms in the %d reports ending at offset: %s' % (W, ' '.join(gaps)))
