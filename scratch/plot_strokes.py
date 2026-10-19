#!/usr/bin/env python3
"""plot_strokes.py CSV EVENTS.txt OUT_PREFIX [--min-ms 300]: grids of strokes (touches >= --min-ms) with pressure samples and the pressed
state from `rt replay CSV --events > EVENTS.txt` (green = pressed, green / red lines = press / release). Needs matplotlib (the system
python has it). Look at the PNGs with an image viewer or Claude's Read tool."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches, rt_events  # noqa: E402
import matplotlib  # noqa: E402
matplotlib.use('Agg')
import matplotlib.pyplot as plt  # noqa: E402

args = sys.argv[1:]
d = load(args[0])
ev = [(t, e) for _, t, e, _ in rt_events(open(args[1]).read())]
minms = float(args[args.index('--min-ms') + 1]) if '--min-ms' in args else 300
T, P = d['eng'], d['p']
t0 = T[0]
ev = [(t + t0, e) for t, e in ev]
sel = [(a, b) for a, b in touches(P) if T[b - 1] - T[a] >= minms]
for page in range(0, len(sel), 12):
    fig, axs = plt.subplots(4, 3, figsize=(18, 13))
    axs = axs.ravel()
    for ax, (a, b) in zip(axs, sel[page:page + 12]):
        ts = T[a]
        xs, ys, last = [], [], None
        for k in range(max(0, a - 10), min(len(P), b + 10)):
            if P[k] != last:
                xs.append(T[k] - ts); ys.append(P[k]); last = P[k]
        ax.plot(xs, ys, '-', lw=0.8, color='k', marker='.', ms=2)
        x0 = None
        for t, e in ev:
            if T[max(0, a - 10)] - 1 <= t <= T[min(len(P) - 1, b + 10)] + 1:
                if e in ('Contact', 'Rearm'):
                    x0 = t - ts; ax.axvline(x0, color='g', lw=0.6)
                else:
                    ax.axvline(t - ts, color='r', lw=0.6)
                    if x0 is not None:
                        ax.axvspan(x0, t - ts, color='g', alpha=0.12); x0 = None
        ax.set_title('t %.1f s (%d ms)' % (ts / 1000, T[b - 1] - ts), fontsize=9)
        ax.set_ylim(-200, 8400)
    fig.tight_layout()
    fig.savefig('%s-%02d.png' % (args[2], page // 12), dpi=70)
    plt.close(fig)
print(len(sel), 'strokes plotted')
