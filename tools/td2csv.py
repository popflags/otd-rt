#!/usr/bin/env python3
"""td2csv.py TABLET_DATA.txt OUT.csv: an OpenTabletDriver Tablet Debugger recording (tablet-data_<unix s>.txt) of the PTK-670 custom
firmware -> a CSV that `rt replay` / tools/Tune read: t_ms (host time, the sum of OTD's Deltas), raw pressure, engine_ms (the time
RapidTriggerFilter gives the engine with Firmware Sample Clock on: byte-17 counter steps x 2.000 ms, host time capped at 50 ms for the
first report and after gaps >= 50 ms). Only 0x1E reports reach the engine; other reports (aux) only take host time.
Windows debugger Deltas are tight; Linux ones come in 0.1 / 2 ms pairs (a debugger artefact), which engine_ms does not depend on."""
import re
import sys

SAMPLE_MS, CAP_MS = 2.000, 50.0
inp, out = sys.argv[1], sys.argv[2]
t = 0.0
last_t = None
counter = -1
engine = 0.0
n = 0
with open(out, 'w') as o:
    o.write('t_ms,raw,engine_ms\n')
    for line in open(inp, errors='replace'):
        m = re.search(r'\{ ([0-9A-Fa-f ]+) \}, Delta:([0-9.]+)ms', line)
        if not m:
            continue
        b = bytes.fromhex(m.group(1))
        t += float(m.group(2))
        if b[0] != 0x1E or len(b) < 18:
            continue
        host = 0.0 if last_t is None else min(t - last_t, CAP_MS)
        last_t = t
        elapsed = host if counter < 0 or host >= CAP_MS else min(((b[17] - counter) & 255) * SAMPLE_MS, CAP_MS)
        counter = b[17]
        engine += elapsed
        o.write('%.3f,%d,%.3f\n' % (t, b[9] | b[10] << 8, engine))
        n += 1
print('%s: %d reports, host %.1f s, engine %.1f s' % (out, n, t / 1000, engine / 1000))
