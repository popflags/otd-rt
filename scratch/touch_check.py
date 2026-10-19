#!/usr/bin/env python3
"""touch_check.py CSV [--rt PATH_TO_rt.dll] [--dotnet PATH] [--set Name=Value ...]: replays the log with rt and checks that every touch
with pressure gets pressed, and how soon after its first sample. Works on td2csv / diagnostics / td_full CSVs (rt reads engine_ms).
Unpressed touches are listed with their samples (lone 8191 readings = firmware phantoms; check hover with phantoms.py)."""
import os
import subprocess
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches, rt_events  # noqa: E402

args = sys.argv[1:]
csvp = args[0]
repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rt = args[args.index('--rt') + 1] if '--rt' in args else os.path.join(repo, 'tools/RtTool/bin/Release/net8.0/rt.dll')
dotnet = args[args.index('--dotnet') + 1] if '--dotnet' in args else 'dotnet'
sets = [a for k, a in enumerate(args) if k > 0 and args[k - 1] == '--set']
cmd = [dotnet, rt, 'replay', csvp, '--events'] + sum([['--set', s] for s in sets], [])
ev = [(t, e) for _, t, e, _ in rt_events(subprocess.run(cmd, capture_output=True, text=True).stdout)]
d = load(csvp)
P, E = d['p'], d['eng']
t0 = E[0]
times = [t + t0 for t, _ in ev]
import bisect  # noqa: E402
state = []
s = False
for _, e in ev:
    s = e in ('Contact', 'Rearm')
    state.append(s)
miss, late, n = [], [], 0
for a, b in touches(P):
    n += 1
    first = None
    for k in range(a, b):
        j = bisect.bisect_right(times, E[k]) - 1
        if j >= 0 and state[j]:
            first = k
            break
    if first is None:
        miss.append((E[a], E[b - 1] - E[a], max(P[a:b]), P[a:min(b, a + 8)]))
    elif E[first] > E[a]:
        late.append((E[a], E[first] - E[a], P[a]))
print('%d touches with pressure: pressed %d, never pressed %d, pressed after the first sample %d' % (n, n - len(miss), len(miss), len(late)))
for t, dur, pk, seq in miss:
    print('   not pressed at %.2f s (engine clock), %.0f ms, peak %d: %s' % (t / 1000, dur, pk, seq))
for t, dl, f in late:
    print('   late by %.0f ms at %.2f s, first sample %d' % (dl, t / 1000, f))
