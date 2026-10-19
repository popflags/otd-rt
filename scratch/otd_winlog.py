#!/usr/bin/env python3
"""otd_winlog.py LOG.json: reads an OpenTabletDriver daemon log (Windows: AppData/Local/OpenTabletDriver/Logs/*.log.json, a JSON array
that is not closed while OTD runs) and prints plugin loads / installs, applied Rapid Trigger filter settings, output mode, warnings and
errors. Use it to confirm which plugin version and settings a recorded session ran (e.g. the settings line includes GlitchJump only
on >= 2.10.0). Installing a plugin through OTD's plugin manager while the daemon runs logs 'already loaded' but did apply on
2026-10-19."""
import json
import sys

txt = open(sys.argv[1], encoding='utf-8-sig').read().rstrip().rstrip(',')
if not txt.endswith(']'):
    txt += ']'
for e in json.loads(txt):
    m = str(e.get('Message', ''))
    if e.get('Level', 0) >= 2 or e.get('Group') == 'Plugin' and e.get('Level', 0) >= 1 or 'Rapid Trigger' in m or 'Output mode' in m \
            or 'RapidTrigger' in m or 'debugging' in m:
        print(e.get('Time', '')[11:23], e.get('Level'), e.get('Group'), m[:400].replace('\n', ' | '))
