"""button_releases.py CSV ARGSFILE [--set Name=V ...]: replays a CSV with a buttons column (tools/td2csv.py, diagnostics) through rt
with the settings in ARGSFILE (e.g. scratch/owner-settings.args) plus --set, and classifies every side-button change while the pen
is down (pressure > 2 for 8 reports either side): no release within 10 reports either side, a release that re-presses within 60
reports, or one that stays up. DETAIL=1 lists the releases with their offset from the change. Used to choose Button Settle Reports."""
import csv, subprocess, sys, bisect, os
from collections import Counter
csvf, argsf = sys.argv[1], sys.argv[2]
extra = sys.argv[3:]
rows = list(csv.DictReader(open(csvf)))
eng = [float(r['engine_ms']) for r in rows]
t0 = eng[0]
p = [int(r['raw']) for r in rows]
btn = [int(r['buttons']) for r in rows]
args = open(argsf).read().split()
out = subprocess.run(['dotnet' if False else os.path.expanduser('~/.cache/otd-rt-tools/dotnet/dotnet'), 'tools/RtTool/bin/Release/net8.0/rt.dll', 'replay', csvf] + args + extra + ['--events'],
                     capture_output=True, text=True).stdout
ev = []
for line in out.splitlines():
    parts = line.split()
    if len(parts) >= 4 and parts[1] == 'ms' and parts[2] in ('Contact', 'Rearm', 'Release', 'Lift', 'FastRelease'):
        t = float(parts[0]) + t0
        i = bisect.bisect_left(eng, t - 0.05)
        ev.append((i, parts[2]))
summary = [l for l in out.splitlines() if 'presses' in l]
evidx = [e[0] for e in ev]
ch = [i for i in range(10, len(rows) - 12) if btn[i] != btn[i - 1] and all(p[j] > 2 for j in range(i - 8, i + 8))]
res = Counter()
detail = []
for i in ch:
    lo, hi = bisect.bisect_left(evidx, i - 10), bisect.bisect_right(evidx, i + 10)
    near = ev[lo:hi]
    rel = [e for e in near if e[1] in ('Release', 'FastRelease')]
    if not rel:
        res['no release'] += 1
        continue
    after = [e for e in ev[lo:] if e[0] > rel[0][0]][:1]
    repress = after and after[0][1] == 'Rearm' and after[0][0] - rel[0][0] < 60
    kind = '%02X->%02X' % (btn[i - 1], btn[i])
    res['release, re-pressed' if repress else 'release, stays up'] += 1
    detail.append('%8.2f s %s rel at %+d (%s)%s' % ((eng[i] - t0) / 1000, kind, rel[0][0] - i, rel[0][1], ' re-press %+d' % (after[0][0] - i) if repress else ''))
print(os.path.basename(csvf), ' '.join(extra) or '(no --set)', '| touching button changes', len(ch), dict(res))
print('   ', summary[0].strip() if summary else '')
if os.environ.get('DETAIL'):
    for d in detail: print('     ', d)
