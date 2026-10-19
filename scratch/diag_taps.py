#!/usr/bin/env python3
"""diag_taps.py DIAGNOSTICS.csv [--runs] [--from S --to S]: tap timing from a live diagnostics CSV (needs the pressed/event columns).

Per touch (pressure > 0 run from 0): onset, press, release, lift on the PC clock (t_ms) and the engine clock (engine_ms).
Prints: presses later than the touch's first report (with the reason to look at), click length / release-before-lift stats, and with
--runs the rhythmic runs (>= 8 taps whose onset intervals stay within 15 % CV) with their interval median, steadiest 15-tap stretch and
whether press-to-press equals touch-to-touch. Intervals use the PC clock: the engine clock only counts <= 50 ms for gaps where the
pen left hover range. Used on rt-20261020-002836.csv (2026-10-20): 933 / 935 presses on the first report, rhythm median 520 ms."""
import os
import statistics as st
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches, q  # noqa: E402

args = sys.argv[1:]
d = load(args[0])
if 'event' not in d:
    sys.exit('needs a diagnostics CSV (pressed/event columns); for td2csv logs use touch_check.py')
lo = float(args[args.index('--from') + 1]) * 1000 if '--from' in args else -1e18
hi = float(args[args.index('--to') + 1]) * 1000 if '--to' in args else 1e18
P, H, E, EV = d['p'], d['host'], d['eng'], d['event']
taps = []
for a, b in touches(P):
    if not lo <= H[a] <= hi:
        continue
    press = [k for k in range(a, b) if EV[k] in (1, 2)]
    rel = [k for k in range(a, min(len(P), b + 40)) if EV[k] in (3, 4, 5)]
    taps.append(dict(a=a, b=b, press=press, rel=rel, first=P[a], peak=max(P[a:b])))
real = [t for t in taps if t['press']]
print('%d touches, %d pressed, %d never pressed (lone 8191 phantoms show up here)' % (len(taps), len(real), len(taps) - len(real)))
late = [t for t in real if t['press'][0] != t['a']]
print('presses later than the first report: %d' % len(late))
for t in late:
    k = t['press'][0]
    print('   %.3f s (PC): first sample %d, pressed %.1f ms later at %d' % (H[t['a']] / 1000, t['first'], E[k] - E[t['a']], P[k]))
cl = [H[t['rel'][0]] - H[t['press'][0]] for t in real if t['rel']]
rl = [H[t['b']] - H[t['rel'][0]] for t in real if t['rel'] and t['b'] < len(P) and t['rel'][0] < t['b']]
print('click length p10/50/90 %.0f / %.0f / %.0f ms; release before the pen leaves p10/50/90 %.0f / %.0f / %.0f ms' % (
    q(cl, .1), q(cl, .5), q(cl, .9), q(rl, .1), q(rl, .5), q(rl, .9)))
if '--runs' in args:
    on = [H[t['a']] for t in real]
    i, runs = 0, []
    while i < len(on) - 8:
        j = i + 2
        while j < len(on):
            iv = [on[k] - on[k - 1] for k in range(i + 1, j + 1)]
            if iv[-1] > 1500 or st.pstdev(iv) / st.mean(iv) > 0.15:
                break
            j += 1
        if j - i >= 8:
            runs.append((i, j))
            i = j
        else:
            i += 1
    allv = []
    for i, j in runs:
        iv = [on[k] - on[k - 1] for k in range(i + 1, j)]
        pr = [H[real[k]['press'][0]] - H[real[k - 1]['press'][0]] for k in range(i + 1, j)]
        allv += iv
        w = min((iv[k:k + 15] for k in range(max(1, len(iv) - 14))), key=lambda x: st.pstdev(x))
        same = sum(1 for x, y in zip(iv, pr) if abs(x - y) < 0.01)
        print('run at %7.1f s: %3d taps, interval median %.0f ms (sd %.0f), steadiest 15: %.0f ms (sd %.0f), press-to-press = touch-to-touch %d / %d' % (
            on[i] / 1000, j - i, st.median(iv), st.pstdev(iv), st.mean(w), st.pstdev(w), same, len(iv)))
    if allv:
        print('all rhythmic intervals: median %.0f ms (%.0f per minute)' % (st.median(allv), 60000 / st.median(allv)))
