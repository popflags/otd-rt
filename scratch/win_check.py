#!/usr/bin/env python3
"""win_check.py [--since 'YYYY-MM-DD HH:MM'] [--lib DIR]: is the Windows partition safe to write from Linux?

Reads Windows' System.evtx (copy it first: /run/media/popf/SapphireOS/Windows/System32/winevt/Logs/System.evtx, from a read-only
mount if needed) and lists boots / shutdowns / NTFS health since --since (UTC). Needs python-evtx: pip install --target DIR
python-evtx, then pass --lib DIR. Safe when: the last session ends with User32 1074 + EventLog 6006, no Ntfs 55 after the last boot's
Ntfs 98 (CorruptionActionState 0), no EventLog 6008 at the following boot, and ntfs3 mounts it read-write (a dirty volume refuses).
Kernel-General 13 (shutdown time) is written at the NEXT boot, so it cannot confirm the last shutdown before Windows boots again.
History: 2026-10-19 a restart that never finished (1074 + 6006, then 6008 at the next boot) followed by Linux writes -> 60 NTFS
index-entry repairs and a dirty volume; fixed by chkdsk."""
import re
import sys

args = sys.argv[1:]
lib = args[args.index('--lib') + 1] if '--lib' in args else None
since = args[args.index('--since') + 1] if '--since' in args else '2000-01-01'
path = [a for k, a in enumerate(args) if not a.startswith('--') and (k == 0 or not args[k - 1].startswith('--'))]
path = path[0] if path else 'System.evtx'
if lib:
    sys.path.insert(0, lib)
import Evtx.Evtx as evtx  # noqa: E402

with evtx.Evtx(path) as log:
    for rec in log.records():
        x = rec.xml()
        t = re.search(r'SystemTime="([^"]+)"', x).group(1)[:19]
        if t < since:
            continue
        prov = re.search(r'Provider Name="([^"]+)"', x).group(1)
        eid = re.search(r'<EventID[^>]*>(\d+)</EventID>', x).group(1)
        keep = (prov == 'EventLog' and eid in ('6005', '6006', '6008')) or (prov.endswith('User32') and eid == '1074') or \
               (prov.endswith('Kernel-General') and eid in ('12', '13')) or (prov.endswith('Ntfs') and eid in ('55', '98', '130', '133'))
        if keep:
            data = re.findall(r'<Data Name="(param5|CorruptionActionState|DriveName)">([^<]*)</Data>', x)
            print(t, 'UTC', prov.replace('Microsoft-Windows-', ''), eid, ' '.join('%s=%s' % kv for kv in data))
