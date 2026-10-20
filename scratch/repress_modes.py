#!/usr/bin/env python3
"""repress_modes.py CSV... [--dotnet D] [--rt rt.dll] [--set Name=Value ...]: re-presses and drag cut time for one set of settings, to
compare Activation Distance from normal (10) to "no re-press until lift" (10999) and anything between.

Replays each log with rt. Per re-press without lifting: its progression (highest pressure before the next release minus the
activation point trough + Activation Distance; < 100 = a wobble click, >= 100 = a real re-tap or a recovered press) and how long after
the trough it fired. Per release (not a lift) inside a drag (touch >= 250 ms): it is a cut when, before pressure falls under 25 % of the
press's peak, it comes back up by >= 100 or holds level (falls < 3 % of the peak over 40 ms); the button is then up until the next
press or until pressure falls under 25 % of the peak. Prints one summary line. Used 2026-10-20 to tune the owner's no-re-press mode."""
import bisect
import os
import re
import subprocess
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches, q  # noqa: E402

args = sys.argv[1:]
repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rt = args[args.index('--rt') + 1] if '--rt' in args else os.path.join(repo, 'tools/RtTool/bin/Release/net8.0/rt.dll')
dotnet = args[args.index('--dotnet') + 1] if '--dotnet' in args else 'dotnet'
sets = [a for k, a in enumerate(args) if k > 0 and args[k - 1] == '--set']
files = [a for k, a in enumerate(args) if not a.startswith('--') and (k == 0 or args[k - 1] not in ('--set', '--rt', '--dotnet'))]
val = dict(s.split('=') for s in sets)
act = float(val.get('ActivationDistance', 10))
EV = re.compile(r'^\s+([0-9.]+) ms\s+(\w+)\s+pressure\s+(\d+)')

wobble, real, delay, cuts, cut_ms, contacts, tap_lead = 0, 0, [], 0, [], 0, []
for path in files:
    out = subprocess.run([dotnet, rt, 'replay', path, '--events'] + sum([['--set', s] for s in sets], []),
                         capture_output=True, text=True).stdout
    d = load(path)
    P, E, H = d['p'], d['eng'], d['host']
    t0 = E[0]
    ev = []
    for line in out.splitlines():
        m = EV.match(line)
        if m:
            t, p = float(m.group(1)) + t0, int(m.group(3))
            k = bisect.bisect_left(E, t - 0.06)
            while k < len(E) and not (P[k] == p and abs(E[k] - t) < 0.06):
                k += 1
            if k < len(E):
                ev.append((k, m.group(2)))
    tb = touches(P)
    starts = [a for a, _ in tb]
    for i, (k, e) in enumerate(ev):
        ti = bisect.bisect_right(starts, k) - 1
        if ti < 0:
            continue
        a, b = tb[ti]
        nxt = next((kk for kk, ee in ev[i + 1:] if kk < b), None)
        if e == 'Contact':
            contacts += 1
        elif e == 'Rearm':
            r = next(kk for kk, ee in reversed(ev[:i]) if ee in ('Release', 'FastRelease'))
            trough_i = min(range(r, k + 1), key=lambda j: P[j])
            end = nxt if nxt is not None else b
            prog = max(P[k:end + 1]) - (P[trough_i] + act)
            if prog >= 100:
                real += 1
                delay.append(H[k] - H[trough_i])
            else:
                wobble += 1
        elif e in ('Release', 'FastRelease'):
            dur = H[b - 1] - H[a]
            if dur < 250:
                if nxt is None:
                    tap_lead.append(H[b - 1] - H[k])
                continue
            pressed_at = next(kk for kk, ee in reversed(ev[:i]) if ee in ('Contact', 'Rearm'))
            peak = max(P[pressed_at:k + 1])
            # A cut when, before pressure falls under 25 % of the peak, it comes back up (>= 100 above its low) or holds level
            # (falls < 3 % of the peak over 40 ms): the pen keeps pressing. A steady fall to the lift is a real release. The button
            # is up from the release until the next press or until pressure falls under 25 % of the peak.
            j, low, cut = k, P[k], False
            while j < b and P[j] > 0.25 * peak:
                low = min(low, P[j])
                jj = bisect.bisect_left(H, H[j] + 40, j, b)
                if P[j] - low >= 100 or (jj < b and P[jj] > 0.25 * peak and P[j] - P[jj] < 0.03 * peak):
                    cut = True
                j += 1
            if cut:
                stop = min(nxt if nxt is not None else b - 1, j)
                cuts += 1
                cut_ms.append(H[stop] - H[k])

n = real + wobble
print('Activation %-6g re-presses %3d (real %3d, wobble clicks %3d), real re-press %4.1f ms after the trough (p50) | drag cuts %3d, '
      'button up while pressing p50 %3.0f / p90 %3.0f ms, total %5.1f s | tap release lead p50 %2.0f ms' % (
          act, n, real, wobble, q(delay, .5) if delay else float('nan'), cuts, q(cut_ms, .5) if cut_ms else 0,
          q(cut_ms, .9) if cut_ms else 0, sum(cut_ms) / 1000, q(tap_lead, .5) if tap_lead else float('nan')))
