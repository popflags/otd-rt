"""Shared loaders for the analysis scripts in scratch/ (stdlib only).

Formats handled by load():
  diagnostics CSV (plugin, Enable Diagnostics):  t_ms,raw,pressed,event,anchor,hold_reference,release_distance,fall_excess,engine_ms
  tools/td2csv.py output:                         t_ms,raw,engine_ms
  scratch/td_full.py output:                      engine_ms,p,x,y,st,hd,host_ms
Returns a dict of lists: host (PC clock, ms), eng (engine / tablet counter clock, ms), p (raw pressure), and when present
pressed, event (live plugin output), x, y, st (status byte), hd (hover distance).
Event codes (TriggerEvent): 0 None, 1 Contact, 2 Rearm, 3 Release, 4 Lift, 5 FastRelease.
"""
import csv
import re

EVENTS = {0: 'None', 1: 'Contact', 2: 'Rearm', 3: 'Release', 4: 'Lift', 5: 'FastRelease'}


def load(path):
    with open(path, newline='') as f:
        rd = csv.reader(f)
        head = [h.strip() for h in next(rd)]
        ix = {h: i for i, h in enumerate(head)}
        out = {k: [] for k in ('host', 'eng', 'p', 'pressed', 'event', 'x', 'y', 'st', 'hd')}
        pcol = ix.get('raw', ix.get('p'))
        for r in rd:
            if not r:
                continue
            out['p'].append(int(r[pcol]))
            out['eng'].append(float(r[ix['engine_ms']]) if 'engine_ms' in ix else float(r[ix['t_ms']]))
            out['host'].append(float(r[ix['t_ms']]) if 't_ms' in ix else float(r[ix['host_ms']]) if 'host_ms' in ix else out['eng'][-1])
            if 'pressed' in ix:
                out['pressed'].append(r[ix['pressed']] == '1')
                out['event'].append(int(r[ix['event']]))
            for k in ('x', 'y', 'st', 'hd'):
                if k in ix:
                    out[k].append(int(r[ix[k]]))
        return {k: v for k, v in out.items() if v}


def touches(p):
    """(start, end) report index pairs of runs with pressure > 0 that start from 0 (end is exclusive)."""
    res, i, n = [], 0, len(p)
    while i < n:
        if p[i] > 0 and (i == 0 or p[i - 1] == 0):
            j = i
            while j < n and p[j] > 0:
                j += 1
            res.append((i, j))
            i = j
        else:
            i += 1
    return res


def rt_events(text):
    """Parse `rt replay ... --events` output into [(file, time_ms, event_name, pressure)]."""
    out, cur = [], None
    for line in text.splitlines():
        if line.startswith('== '):
            cur = line[3:].split(':')[0]
        m = re.match(r'^\s+([0-9.]+) ms\s+(\w+)\s+pressure\s+(\d+)', line)
        if m:
            out.append((cur, float(m.group(1)), m.group(2), int(m.group(3))))
    return out


def q(values, frac):
    v = sorted(values)
    return v[min(len(v) - 1, int(frac * (len(v) - 1) + 0.5))] if v else float('nan')
