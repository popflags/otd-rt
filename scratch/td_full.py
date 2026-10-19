#!/usr/bin/env python3
"""td_full.py TABLET_DATA.txt OUT.csv: OTD Tablet Debugger recording -> per-report CSV with everything the analyses need:
engine_ms (as RapidTriggerFilter with Firmware Sample Clock: byte-17 counter steps x 2.000 ms, host time capped at 50 ms on the first
report and after gaps >= 50 ms), p (pressure), x, y, st (status byte 2: bit0 tip, bit6 near proximity, bit7 tracked), hd (hover
distance, byte 19; 255 = none), host_ms (sum of OTD's Deltas). Only 0x1E pen reports. tools/td2csv.py writes the lean replay format."""
import re
import sys

t = 0.0
last = None
ctr = -1
eng = 0.0
with open(sys.argv[2], 'w') as o:
    o.write('engine_ms,p,x,y,st,hd,host_ms\n')
    for ln in open(sys.argv[1], errors='replace'):
        m = re.search(r'\{ ([0-9A-Fa-f ]+) \}, Delta:([0-9.]+)ms', ln)
        if not m:
            continue
        b = bytes.fromhex(m.group(1))
        t += float(m.group(2))
        if b[0] != 0x1E or len(b) < 20:
            continue
        host = 0.0 if last is None else min(t - last, 50.0)
        last = t
        el = host if ctr < 0 or host >= 50 else min(((b[17] - ctr) & 255) * 2.0, 50.0)
        ctr = b[17]
        eng += el
        o.write('%.3f,%d,%d,%d,%d,%d,%.3f\n' % (eng, b[9] | b[10] << 8, int.from_bytes(b[3:6], 'little'),
                                                int.from_bytes(b[6:9], 'little'), b[2], b[19], t))
