#!/usr/bin/env python3
"""norearm_cuts.py CSV... [--dotnet D] [--rt rt.dll] [--set Name=Value ...] [--list] [--hold-ms 40] [--frac 0.25]: drag cut-outs in a
"no re-press until lift" setup (Activation Distance huge, e.g. 10999: a release can only be followed by a press after a lift).

Replays each log with rt and the given settings. A release (fast or slow path, not a lift) is a cut-out when the pen keeps pressing
afterwards: before pressure falls under --frac (25 %) of the press's peak, it comes back up by >= 100 raw, or holds level (falls less
than 3 % of the peak over --hold-ms, 40 ms). A steady fall to the lift, however slow, is a real release. Without re-presses the
button then stays up while the pen is still down. Each cut-out is placed in its touch: landing (< 100 ms after the press), drag end
(touch >= 400 ms and within its last 300 ms), else mid. Cost side: release lead before the pen lifts for taps (touches < 250 ms)
and drag ends that are not cut-outs (larger = earlier). Used 2026-10-20 after the owner played Activation Distance 10999."""
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
hold_ms = float(args[args.index('--hold-ms') + 1]) if '--hold-ms' in args else 40
frac = float(args[args.index('--frac') + 1]) if '--frac' in args else 0.25
opts = ('--set', '--rt', '--dotnet', '--hold-ms', '--frac')
sets = [a for k, a in enumerate(args) if k > 0 and args[k - 1] == '--set']
files = [a for k, a in enumerate(args) if not a.startswith('--') and (k == 0 or args[k - 1] not in opts)]
EV = re.compile(r'^\s+([0-9.]+) ms\s+(\w+)\s+pressure\s+(\d+)')

cuts, tap_lead, end_lead, presses, contacts = [], [], [], 0, 0
for path in files:
    out = subprocess.run([dotnet, rt, 'replay', path, '--events'] + sum([['--set', s] for s in sets], []),
                         capture_output=True, text=True).stdout
    d = load(path)
    P, E, H = d['p'], d['eng'], d['host']
    t0 = E[0]
    tb = touches(P)
    starts = [a for a, _ in tb]
    pressed_at = None
    for line in out.splitlines():
        m = EV.match(line)
        if not m:
            continue
        t, e, p = float(m.group(1)) + t0, m.group(2), int(m.group(3))
        k = bisect.bisect_left(E, t - 0.06)
        while k < len(E) and not (P[k] == p and abs(E[k] - t) < 0.06):
            k += 1
        if k >= len(E):
            continue
        if e in ('Contact', 'Rearm'):
            presses += 1
            contacts += e == 'Contact'
            pressed_at = k
            continue
        ti = bisect.bisect_right(starts, k) - 1
        if ti < 0 or pressed_at is None:
            continue
        a, b = tb[ti]
        dur = H[b - 1] - H[a]
        lead = H[b - 1] - H[k] if k < b else 0
        if e == 'Lift':
            continue
        peak = max(P[pressed_at:k + 1])
        # Before pressure falls under frac of the peak: does it come back up (>= 100 above its low since the release), or hold
        # level (falls less than 3 % of the peak over hold_ms)? A steady fall to the lift, however slow, is a real release.
        j, low, kind = k, P[k], None
        while j < b and P[j] > frac * peak:
            low = min(low, P[j])
            if P[j] - low >= 100:
                kind = 'came back'
                break
            jj = bisect.bisect_left(H, H[j] + hold_ms, j, b)
            if jj < b and P[jj] > frac * peak and P[j] - P[jj] < 0.03 * peak:
                kind = 'held'
                break
            j += 1
        since = H[k] - H[pressed_at]
        if kind:
            where = 'landing' if since < 100 else 'drag end' if dur >= 400 and lead <= 300 else 'mid'
            cuts.append(dict(file=os.path.basename(path), t=H[k] / 1000, where=where, since=since, kind=kind, lead=lead, ev=e,
                             peak=peak, p=P[k], after=max(P[k:b]) if k < b else 0))
        elif dur < 250:
            tap_lead.append(lead)
        elif dur >= 400:
            end_lead.append(lead)

w = {x: sum(1 for c in cuts if c['where'] == x) for x in ('landing', 'mid', 'drag end')}
w['back'] = sum(1 for c in cuts if c['kind'] == 'came back')
print('presses %d (contacts %d) | cut-outs %d (landing %d, mid %d, drag end %d; came back %d), button up while pressing p50 %.0f ms | '
      'release lead before lift: taps p50 %.0f mean %.1f ms, drag ends p50 %.0f mean %.1f ms' % (
          presses, contacts, len(cuts), w['landing'], w['mid'], w['drag end'], w['back'], q([c['lead'] for c in cuts], .5),
          q(tap_lead, .5), sum(tap_lead) / max(1, len(tap_lead)), q(end_lead, .5), sum(end_lead) / max(1, len(end_lead))))
if '--list' in args:
    for c in cuts:
        print('   %-36s %8.2f s  %-8s %-9s %-11s %4.0f ms after the press, peak %5d, released at %5d, highest after %5d, pen down %4.0f ms more' % (
            c['file'], c['t'], c['where'], c['kind'], c['ev'], c['since'], c['peak'], c['p'], c['after'], c['lead']))
