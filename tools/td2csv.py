#!/usr/bin/env python3
"""td2csv.py [--analog] TABLET_DATA.txt OUT.csv: an OpenTabletDriver Tablet Debugger recording (tablet-data_<unix s>.txt) of the PTK-670
custom firmware -> a CSV that `rt replay` / tools/Tune read: t_ms (host time, the sum of OTD's Deltas), raw pressure, engine_ms (the time
RapidTriggerFilter gives the engine with Firmware Sample Clock on: byte-17 counter steps x 2.000 ms, host time capped at 50 ms for the
first report and after gaps >= 50 ms), buttons (status byte 2 bits 1-3) and gap (1 = the analog gap rule added host time the counter
did not see). Only 0x1E reports reach the engine; other reports (aux) only take host time.
--analog: the analog pen (CP923) the way the plugin's Analog Pen toggle times it: 1.000 ms per counter step, plus the gap rule (when
host time runs more than 1.5 ms ahead of the counter, the difference is added; early reports count against it, so bursts do not).
Windows debugger Deltas are tight; Linux ones come in 0.1 / 2 ms pairs or bursts (a debugger artefact), which engine_ms does not depend
on, except through the analog gap rule."""
import re
import sys

CAP_MS, GAP_SLACK_MS = 50.0, 1.5
args = [a for a in sys.argv[1:] if a != '--analog']
analog = len(args) != len(sys.argv) - 1
sample_ms = 1.000 if analog else 2.000
inp, out = args[0], args[1]
t = 0.0
last_t = None
counter = -1
engine = 0.0
lag = 0.0
n = gaps = 0
with open(out, 'w') as o:
    o.write('t_ms,raw,engine_ms,buttons,gap\n')
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
        gap = 0
        if counter < 0 or host >= CAP_MS:
            elapsed = host
            lag = 0.0
        else:
            elapsed = ((b[17] - counter) & 255) * sample_ms
            if analog:
                lag = max(lag + host - elapsed, -GAP_SLACK_MS)
                if lag > GAP_SLACK_MS:
                    elapsed += lag
                    lag = 0.0
                    gap = 1
            elapsed = min(elapsed, CAP_MS)
        counter = b[17]
        engine += elapsed
        o.write('%.3f,%d,%.3f,%d,%d\n' % (t, b[9] | b[10] << 8, engine, b[2] & 0x0E, gap))
        n += 1
        gaps += gap
print('%s: %d reports, host %.1f s, engine %.1f s%s' % (out, n, t / 1000, engine / 1000, ', %d gaps' % gaps if analog else ''))
