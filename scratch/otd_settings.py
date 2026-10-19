#!/usr/bin/env python3
"""otd_settings.py SETTINGS.json [Name=Value ...] [--show]: sets Rapid Trigger values in the Wacom PTK-670 profile of an OpenTabletDriver
settings.json by editing the text in place (keeps CRLF line endings on the Windows file, indentation and everything else), inserting
a property that is missing (new settings of a newer DLL) right after DropoutPressure, and checking the result parses. Back up first:
  Linux:   ~/.config/OpenTabletDriver/settings.json
  Windows: /run/media/popf/SapphireOS/Users/Administrator/AppData/Local/OpenTabletDriver/settings.json (backups in its Backup/)
Values: numbers, or true/false. --show prints the current values. Output mode / bindings are never touched (Linux Relative Mode +
MouseBinding, Windows VMulti Relative + VMultiButtonHandler: keep them that way)."""
import json
import re
import sys

args = sys.argv[1:]
fn = args[0]
text = open(fn, 'rb').read().decode('utf-8')
nl = '\r\n' if '\r\n' in text else '\n'
p0 = text.index('"Tablet": "Wacom PTK-670"')
f0 = text.index('"Path": "RapidTrigger.RapidTriggerFilter"', p0)
f1 = text.index('"Enable":', f0)
block = text[f0:f1]
if '--show' in args:
    prof = [p for p in json.loads(text)['Profiles'] if p['Tablet'] == 'Wacom PTK-670'][0]
    rt = [f for f in prof['Filters'] if f['Path'] == 'RapidTrigger.RapidTriggerFilter'][0]
    print({s['Property']: s['Value'] for s in rt['Settings']}, '| enabled', rt['Enable'], '|', prof['OutputMode']['Path'])
    sys.exit()
changed = []
for kv in [a for a in args[1:] if '=' in a]:
    k, v = kv.split('=', 1)
    lit = v.lower() if v.lower() in ('true', 'false') else repr(float(v))
    if '"Property": "%s"' % k not in block:
        m = re.search(r'([ \t]*)\{(\r?\n)([ \t]*)"Property": "DropoutPressure",(\r?\n)[ \t]*"Value": [-0-9.eE]+(\r?\n)([ \t]*)\},', block)
        block = block[:m.end()] + nl + '%s{%s%s"Property": "%s",%s%s"Value": %s%s%s},' % (
            m.group(1), nl, m.group(3), k, nl, m.group(3), lit, nl, m.group(1)) + block[m.end():]
        changed.append('%s added = %s' % (k, lit))
        continue
    m = re.search(r'("Property": "%s",\s*"Value": )(true|false|[-0-9.eE]+)' % k, block)
    if m.group(2) != lit:
        block = block[:m.start(2)] + lit + block[m.end(2):]
        changed.append('%s %s -> %s' % (k, m.group(2), lit))
new = text[:f0] + block + text[f1:]
json.loads(new)
open(fn, 'wb').write(new.encode('utf-8'))
print(fn, '\n  ' + '\n  '.join(changed or ['no change']))
