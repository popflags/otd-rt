# scratch/: analysis scripts (kept in git on purpose)

Working scripts from the 2026-10-19/20 sessions, moved here because Claude's scratchpad under `/tmp` is wiped on reboot. They are
not part of the plugin build. Python 3, stdlib only (`plot_strokes.py` needs matplotlib). Run them
from anywhere; paths are arguments. The current state, file locations and procedures are in `docs/HANDOFF.md`.

| Script | What it does |
|---|---|
| `setup.sh [DIR]` | Reinstalls the throwaway tooling: .NET 8 SDK (the system `dotnet` is runtime-only), ILSpy CLI. Default DIR `~/.cache/otd-rt-tools`. |
| `common.py` | Shared loader for the three CSV formats (diagnostics, `tools/td2csv.py`, `td_full.py`), touch splitting, `rt --events` parsing. |
| `td_full.py TABLET_DATA.txt OUT.csv` | OTD Tablet Debugger log to a per-report CSV with engine_ms, pressure, x, y, status, hover distance, host time. `tools/td2csv.py` writes the lean replay format; use this one when hover / tip bit matter. |
| `diag_taps.py DIAG.csv [--runs] [--from S --to S]` | Live diagnostics: per-tap press / release timing on the PC clock, late presses, click lengths, and rhythmic runs (interval median, steadiest stretch, press-to-press = touch-to-touch). |
| `patterns.py DIAG.csv [--gate MS,...] [--split MS]` | Tap / drag sequences (T, TD, TTD …), onset intervals per pattern, re-presses inside taps and drags, and what a Touchdown Gate would hold back per pattern (same rule as the engine). |
| `no_rearm.py CSV... --set ... [--list]` | What a "no re-press until the pen lifts" mode would lose: every re-press without lifting, how far pressure went past the activation point, where in the touch, click length. |
| `owner-settings.args` | The owner's current Rapid Trigger values as `--set` arguments (from the Windows settings.json): `rt replay LOG $(cat scratch/owner-settings.args)`. Refresh it when the owner changes settings. |
| `touch_check.py CSV [--dotnet D] [--rt rt.dll] [--set Name=V ...]` | Replays a log with `rt` and checks that every touch with pressure is pressed, and how soon. |
| `dips.py CSV [--fall 250] [--rebound 100]` | In-stroke dips with their fastest 10 ms fall: how `tools/Tune`'s re-click / drag-dip labels were found. |
| `glitches.py CSV [--min 2500]` | One- or two-sample firmware pressure glitches and the largest genuine per-sample change. Its largest-rise figure also counts what is left of 3-sample glitches. |
| `phantoms.py TD_FULL.csv` | Lone 8191 readings with hover distance, time since the previous lift and the next touch. |
| `otd_settings.py SETTINGS.json [Name=Value ...] [--show]` | Sets Rapid Trigger values in the PTK-670 profile as a text edit (keeps CRLF, inserts new properties). Back the file up first. |
| `otd_winlog.py LOG.json` | OTD daemon log (Windows `Logs/*.log.json`): plugin loads, applied filter settings, warnings. |
| `plot_strokes.py CSV EVENTS.txt OUT_PREFIX [--min-ms 300]` | PNG grids of strokes with the plugin's pressed state from `rt replay --events`. |

Typical session (Windows diagnostics log):

```sh
scratch/setup.sh                                   # once per reboot
cp /run/media/popf/SapphireOS/Users/Administrator/rapid-trigger-logs/rt-<time>.csv /tmp/diag.csv
python3 scratch/diag_taps.py /tmp/diag.csv --runs  # live press / release timing as the plugin did it
~/.cache/otd-rt-tools/dotnet/dotnet tools/RtTool/bin/Release/net8.0/rt.dll replay /tmp/diag.csv --set ...   # what-if with other settings
```
