#!/usr/bin/env python3
"""Generate the synthetic 1000 Hz logs used to choose the defaults.

syn_drag.csv: four held drags (5000/2500/7000/1200) with slow 20-35% dips, 2-3% tremor at 9 Hz and
              +-15 noise. Any release before the final lift of each drag is a cut-out.
syn_tap.csv:  60 rapid taps without lifting (random 800-2000 trough, 3000-6000 peak, 50-90 ms period)
              with +-15 noise.

These are made up, harsher than typical drags on purpose. Real recordings from the tablet
(recordings/) always take priority. Output is in the plugin's diagnostics CSV format.

Usage: python3 tools/synth_logs.py [output_dir]
"""
import math
import os
import random
import sys


def write(path, values):
    with open(path, "w") as f:
        f.write("t_ms,raw,pressed,event,anchor,hold_reference,release_distance,fall_excess\n")
        for i, v in enumerate(values):
            f.write(f"{i:.3f},{max(0, round(v))},0,0,0,0,0,0\n")


def ramp(a, b, n):
    return [a + (b - a) * (i + 1) / n for i in range(n)]


def drag(level, ms, dip_fraction, dip_ms, tremor, noise, seed, tremor_hz=9):
    r = random.Random(seed)
    out = []
    for i in range(ms):
        phase = i % (dip_ms * 2.5)
        dip = math.sin(math.pi * phase / dip_ms) if phase < dip_ms else 0
        out.append(level * (1 - dip_fraction * dip)
                   + math.sin(2 * math.pi * tremor_hz * i / 1000) * tremor * level
                   + (r.random() * 2 - 1) * noise)
    return out


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)

    d = [0] * 50
    for k, (level, dip_fraction, dip_ms, tremor) in enumerate([(5000, .30, 400, .03), (2500, .25, 300, .02),
                                                              (7000, .20, 500, .03), (1200, .35, 250, .02)]):
        d += ramp(0, level, 60) + drag(level, 4000, dip_fraction, dip_ms, tremor, 15, k)
        d += ramp(level, 0, max(10, int(level / 50))) + [0] * 200
    write(os.path.join(out_dir, "syn_drag.csv"), d)

    t = [0] * 50 + ramp(0, 1500, 20)
    r = random.Random(9)
    for _ in range(60):
        hi, lo, n = r.uniform(3000, 6000), r.uniform(800, 2000), int(r.uniform(50, 90))
        t += [lo + (hi - lo) * 0.5 * (1 - math.cos(2 * math.pi * i / n)) + (r.random() * 2 - 1) * 15 for i in range(n)]
    t += ramp(1500, 0, 30) + [0] * 100
    write(os.path.join(out_dir, "syn_tap.csv"), t)

    print(f"wrote {out_dir}/syn_drag.csv ({len(d)} samples) and {out_dir}/syn_tap.csv ({len(t)} samples)")


if __name__ == "__main__":
    main()
