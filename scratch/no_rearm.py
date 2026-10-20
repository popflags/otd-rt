#!/usr/bin/env python3
"""no_rearm.py CSV... [--dotnet D] [--rt rt.dll] [--set Name=Value ...] [--list]: what a "no re-press until the pen lifts" mode would lose.

Replays each log with rt (the given --set values, e.g. the owner's settings) and takes every Rearm (re-press without lifting): with
re-presses only after pressure reaches the lift threshold, each of them is a click that never happens, and the button stays up from
the release before it until the pen lifts. Contacts are unchanged (checked against a replay with ActivationDistance=1e9).
Per re-press: trough since the release, activation point = trough + ActivationDistance + ActivationPercent% of the trough,
progression = highest pressure after the re-press (until the next release) minus the activation point ("a real tap progresses past
the activation point", owner 2026-10-20), where in the touch it happened (landing: < 100 ms after the touch began; drag end: touch
>= 400 ms and within its last 300 ms; else mid), and how long the pen stayed down after it (button up for that long without re-press).
Used 2026-10-20 on all recordings + Windows diagnostics logs."""
import bisect
import collections
import os
import subprocess
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, touches, rt_events, q  # noqa: E402

args = sys.argv[1:]
repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rt = args[args.index('--rt') + 1] if '--rt' in args else os.path.join(repo, 'tools/RtTool/bin/Release/net8.0/rt.dll')
dotnet = args[args.index('--dotnet') + 1] if '--dotnet' in args else 'dotnet'
sets = [a for k, a in enumerate(args) if k > 0 and args[k - 1] == '--set']
files = [a for k, a in enumerate(args) if not a.startswith('--') and (k == 0 or args[k - 1] not in ('--set', '--rt', '--dotnet'))]
setting = dict(s.split('=') for s in sets)
act_d = float(setting.get('ActivationDistance', 10))
act_p = float(setting.get('ActivationPercent', 0.5))
BINS = (0, 25, 50, 100, 200, 500, 1e9)


def replay(path, extra=()):
    cmd = [dotnet, rt, 'replay', path, '--events'] + sum([['--set', s] for s in list(sets) + list(extra)], [])
    return rt_events(subprocess.run(cmd, capture_output=True, text=True).stdout)


def bin_of(v):
    return next(i for i in range(len(BINS) - 1) if BINS[i] <= v < BINS[i + 1])


total = collections.Counter()
allrows = []
for path in files:
    d = load(path)
    P, E, H = d['p'], d['eng'], d['host']
    t0 = E[0]
    ev = replay(path)
    contacts_off = sum(1 for e in replay(path, ['ActivationDistance=1000000000']) if e[2] == 'Contact')

    def row(t, p):
        i = bisect.bisect_left(E, t + t0 - 0.06)
        while i < len(E) and E[i] <= t + t0 + 0.06:
            if P[i] == p:
                return i
            i += 1
        raise ValueError('event at %.1f ms (pressure %d) not found' % (t, p))

    idx = [(row(t, p), e) for _, t, e, p in ev]
    tb = touches(P)
    starts = [a for a, _ in tb]
    contacts = sum(1 for _, e in idx if e == 'Contact')
    rows = []
    for j, (k, e) in enumerate(idx):
        if e != 'Rearm':
            continue
        r = next(i for i, ee in reversed(idx[:j]) if ee in ('Release', 'FastRelease'))
        n = next((i for i, ee in idx[j + 1:] if ee in ('Release', 'FastRelease', 'Lift')), None)
        ti = bisect.bisect_right(starts, k) - 1
        a, b = tb[ti]
        trough = min(P[r:k + 1])
        point = trough + act_d + act_p * 0.01 * trough
        peak = max(P[k:(n if n is not None else b) + 1])
        dur = H[b - 1] - H[a]
        since = H[k] - H[a]
        where = 'landing' if since < 100 else 'drag end' if dur >= 400 and H[b - 1] - H[k] <= 300 else 'mid'
        rows.append(dict(t=H[k] / 1000, prog=peak - point, trough=trough, peak=peak, where=where, down=H[b - 1] - H[k],
                         click=(H[n] - H[k]) if n is not None else float('nan'), dur=dur))
    name = os.path.basename(path)
    real = sum(1 for x in rows if x['prog'] >= 100)
    print('%-40s contacts %4d (same without re-press: %s), re-presses lost %3d: progression <25 %d, 25-50 %d, 50-100 %d, 100-200 %d, 200-500 %d, 500+ %d' % (
        name, contacts, 'yes' if contacts == contacts_off else 'NO %d' % contacts_off, len(rows),
        *[sum(1 for x in rows if bin_of(x['prog']) == i) for i in range(len(BINS) - 1)]))
    total['contacts'] += contacts
    total['rearms'] += len(rows)
    total['real'] += real
    allrows += [dict(x, file=name) for x in rows]
    if '--list' in args:
        for x in rows:
            print('    %8.2f s  %-8s trough %5d peak %5d progression %5.0f  click %5.0f ms  pen down %4.0f ms more (touch %4.0f ms)' % (
                x['t'], x['where'], x['trough'], x['peak'], x['prog'], x['click'], x['down'], x['dur']))

if allrows:
    print('\nALL: %d contacts, %d re-presses without lifting (would be lost)' % (total['contacts'], total['rearms']))
    for lo, hi in zip(BINS, BINS[1:]):
        sel = [x for x in allrows if lo <= x['prog'] < hi]
        if sel:
            w = collections.Counter(x['where'] for x in sel)
            print('  progression %4g-%-5s %4d  (landing %d, mid %d, drag end %d), trough p50 %.0f, click p50 %.0f ms' % (
                lo, '' if hi > 1e8 else '%g' % hi, len(sel), w['landing'], w['mid'], w['drag end'], q([x['trough'] for x in sel], .5),
                q([x['click'] for x in sel if x['click'] == x['click']], .5)))
    near = [x for x in allrows if x['trough'] <= 50]
    print('  troughs <= 50 (pen almost lifted): %d; pen down after the lost re-press p50 %.0f ms, p90 %.0f ms' % (
        len(near), q([x['down'] for x in allrows], .5), q([x['down'] for x in allrows], .9)))
