# Handoff: state, locations, procedures (updated 2026-10-20)

Read this first after a context reset, then `CLAUDE.md` (design history and evidence) and the firmware session's notes
`ptk670-firmware-updates.md` / `ptk670-logs.md` in the repo root (untracked, written by the firmware session). Analysis scripts are in
`scratch/` (see `scratch/README.md`).

## Current state

| What | State |
|---|---|
| Plugin installed | **2.11.1** (`6e0f388`) on Linux and Windows (same DLL, sha256 `349f5dc7c20f9c24…`). |
| Git | Branch `fw-0fc2521a`. GitHub `main` = `cb2474b` (release v2.10.0 published). Local, **not pushed**: `ee201ee` 2.11.0, `6e0f388` 2.11.1. `git push origin fw-0fc2521a:main` publishes v2.11.1 (notes: `release-notes/v2.11.1.md`). |
| Owner's staged files | `recordings/rt-20261014-213607_drag.csv` and `…213849_tap.csv` are staged by the owner and must stay out of commits: always commit with an explicit path list (`git commit -- path…`), never a bare `git commit` / `--amend`. |
| Tablet firmware | Candidate ring500 **a12b3929** (83a52e3c + R5G late-reply fix; hard-press glitches gone), accepted by the owner. 1000 Hz, motion sync ON: a pressure sample every 2.000 ms, repeated on the next report. Lone 8191 "phantom" readings after lifts remain (4–10 % of lifts). |
| Game | S4 League on a private server that reads input at 3000 ticks/s (click length vs frame rate is not an issue). |
| Output | Linux: Relative Mode + MouseBinding Left. Windows: VMulti Relative Mode + VMultiButtonHandler Left. Tip threshold 0 % on both. Keep them different. |
| Other enabled PTK filters | Hover Distance Limiter (PostTransform, 0–255, Near Proximity cutoff on), Scroll Binding (pass-through), Saturn HRRNC (position only). Decompiled 2026-10-20: none drops reports or changes pressure; VMulti writes every button change at once. |
| Diagnostics | **On for Windows** (`C:\Users\Administrator\rapid-trigger-logs\`, ~60 MB per 20 min). Linux off. Turn off when no longer needed. |

### Owner's Rapid Trigger settings (both systems)

Contact Threshold 4 · Phantom Contact Pressure 8191 · Phantom Confirm Time 20 · Dropout Time 4 · Dropout Pressure 8190 · Glitch Jump
2500 · Lift Threshold 2 · Activation Distance 10 · Activation Percent 0 · Fast Fall Speed 12 · Fast Fall Percent 0.2 · Fast Release
Distance 35 · Tap Allowance 0.2 · Re-press Allowance 0.3 · Press Holdoff 12 · Minimum Click 0 · Release Distance 600 · Release Ratio 0
· Max Release Distance 1000 · Drift Time Constant 10 · Press Drift Time Constant 0 · Hold Time 400 · Hold Release Multiplier 1 ·
Firmware Sample Clock on · Preserve Pressure off.

## Locations

| Thing | Path |
|---|---|
| Repo | `/home/popf/Documents/otd_plug/otd-rt` (GitHub `popflags/otd-rt`) |
| Firmware repo | `/home/popf/Documents/otd_plug/ptk670-tapx-1k-fw` (handoff: `docs/handoff/otd-rt-500hz.md`; ~430 tablet-data logs in its root) |
| Linux OTD | settings `~/.config/OpenTabletDriver/settings.json`, plugin `~/.config/OpenTabletDriver/Plugins/RapidTrigger(2)/RapidTrigger(2).dll`, backups `~/.config/OpenTabletDriver/RapidTrigger-<ver>.dll.bak`, `settings.json.before-*`. Daemon: `systemctl --user restart opentabletdriver.service`, log `otd getcurrentlog`. |
| Windows partition | `/dev/nvme0n1p4` (label SapphireOS) → `/run/media/popf/SapphireOS` via `udisksctl mount -b /dev/nvme0n1p4` (add `-o ro` to read only). |
| Windows OTD | `…/SapphireOS/Users/Administrator/AppData/Local/OpenTabletDriver/`: `settings.json` (CRLF), plugin `Plugins/RapidTrigger (2)/RapidTrigger (2).dll` (folder renamed by the owner's plugin-manager install on 2026-10-19; was `RapidTrigger1`), backups in `Backup/`, daemon logs `Logs/*.log.json`, debugger logs `tablet-data_<unix s>.txt`. The old `Plugins/Rapid_Trigger` (Kuuube variant) is disabled for the PTK; leave it. |
| Windows diagnostics | `…/SapphireOS/Users/Administrator/rapid-trigger-logs/rt-<date>-<time>.csv` |
| Windows event log | `…/SapphireOS/Windows/System32/winevt/Logs/System.evtx` |

## Procedures

**Tooling after a reboot.** `scratch/setup.sh` (installs the .NET 8 SDK, python-evtx and ILSpy under `~/.cache/otd-rt-tools`).
The system `dotnet` cannot build. The tool shell is zsh: `$VAR` holding spaces does not split, so wrap loops in `bash -c '…'`.
Do not `cd` into the Windows drive in a shell command.

**Before writing to the Windows partition.** It must mount read-write. ntfs3 refuses a dirty volume; never force it. Copy
`System.evtx` and run `scratch/win_check.py System.evtx --lib ~/.cache/otd-rt-tools/evtx-lib --since '<last session>'`. Write only if
the last session ends with User32 1074 + EventLog 6006, the boot before had Ntfs 98 state 0 and no Ntfs 55 followed. On 2026-10-19 a
restart that never finished, followed by Linux writes, led to 60 NTFS repairs and a dirty volume (fixed with `chkdsk C: /f`).
Keep writes small: the DLL and `settings.json`.

**Install a new plugin build.**
1. `~/.cache/otd-rt-tools/dotnet/dotnet test tests/RapidTrigger.Tests`.
2. Commit, then `dotnet build src/RapidTrigger -c Release --no-incremental` so the DLL's version string carries the commit; check it
   with `strings -el src/RapidTrigger/bin/Release/net8.0/RapidTrigger.dll | grep '^2\.'`.
3. Back up both installed DLLs.
4. Copy the DLL to both plugin paths and `sync`.
5. Linux: restart the daemon and check `otd getcurrentlog` (plugin loaded, `Filter Settings Rapid Trigger: …` shows the values).
6. Windows: loads at the next OTD start. Confirm later with `scratch/otd_winlog.py` on the newest `Logs/*.log.json`.

**Change settings.** Back up, then `scratch/otd_settings.py SETTINGS.json Name=Value …` on each file. It preserves CRLF and inserts new
properties. Restart the Linux daemon.

**Analyse a session.**
- **Windows diagnostics CSV** (has the live plugin output and `engine_ms`): `scratch/diag_taps.py FILE --runs`, plus
  `rt replay FILE --set …` for what-ifs.
- **Tablet Debugger log:** `tools/td2csv.py` (replay format) or `scratch/td_full.py` (hover / tip bit too), then `rt replay`,
  `tools/Tune`, `scratch/touch_check.py`, `dips.py`, `glitches.py`, `phantoms.py`.
- Intervals across range exits must use the PC clock: the engine clock counts at most 50 ms per gap.
- Commit useful recordings as CSV in `recordings/` and keep the raw `tablet-data_*.txt` out (gitignored).

## What happened 2026-10-18 → 20 (details in CLAUDE.md)

| Version | Change | Why |
|---|---|---|
| 2.8.2 | Firmware Sample Clock 2.000 ms | ring500 0fc2521a at 1000 Hz motion sync ON |
| 2.9.0 | A release held by Press Holdoff is dropped if pressure is rising again | landing bounces double-clicked; also first tune on the 2 ms gameplay logs, `tools/Tune` |
| 2.10.0 | Glitch Jump (one-sample jumps ≥ 2500 held up to Dropout Time, ignored if they come back) | firmware 83a52e3c glitches cut drags and once left a hold released 210 ms; Release Distance 300 → 600 (slow path cut drags); Activation 10 + 0 % for 5.3 ms re-press |
| 2.11.0 | Minimum Click (optional, owner uses 0) | wrong hypothesis that short clicks were missed by the game (it reads 3000 ticks/s) |
| 2.11.1 | No dropout / glitch hold while a phantom contact is pending | a real tap 2 ms after a phantom pressed one sample late (live diagnostics) |

Verified on the 2026-10-20 live diagnostics (23 min):
- **Presses:** 933 of 935 came on the touch's first report. The 2.11.1 fix covers one of the other two; the other is a landing at
  exactly 8191 for two samples (phantom guard, 4 ms, kept).
- **Delivery:** press reports reach the PC on the regular 1 ms grid.
- **Releases:** a median 8 ms after the fall starts (±2.5 ms).
- **Rhythm:** about 520 ms between taps (owner intends 500). That spread is the owner's hand, measured the same on both clocks.

## Owner statements that constrain changes

- "Drags must never cut out"; but "drag into a tap is a frequent action i want no delays in". The in-hold release / re-press pairs
  seen on the logs are treated as drag-into-tap, not cuts. No drag cuts were felt on 2.10.0+.
- "Taps cannot be limited if they're genuine". Brushes of 4–6 ms are intended taps; an accidental one registering is fine.
- Initial clicks as fast as possible; re-press delay as low as possible; timing must not be variable.
- 0 light-drag cut-outs is the hard limit when tuning (`tools/Tune`).
- The Windows config is the main one; it may be hand-edited, so read the live file before assuming values.

## Wrong turns (do not repeat)

- **Blaming missed taps on frame-length clicks.** The game reads input at 3000 ticks/s.
- **Release Distance 300.** The slow path then cut slow declines from 8191.
- **Counting every in-hold release / re-press as a cut.** Many are drag-into-tap.
- **A bare `git commit --amend`.** It swept the owner's staged recordings into a commit; they were removed again.
- **Writing to Windows after an unfinished restart.** See the safety check above.
- **Python `open(f, 'w').write(open(f).read()…)`.** It truncates the file before reading it, which emptied `tools/Tune/Program.cs`
  once; it was restored from git.

## Open items

- Push `fw-0fc2521a` to `main` when the owner says so (v2.11.1 release).
- Turn Windows diagnostics off when the owner is done recording.
- Phantom guard: a real landing at exactly 8191 presses on the first sample below it (4 ms, ~1 in 900 taps). Pressing on a lone 8191
  would turn real phantoms (pen 15–20 above the surface) into clicks. Hover distance would separate them, but the engine is
  pressure-only.
- On a12b3929, Glitch Jump and the dropout guard measured zero cost. They are kept as insurance against older firmware.
- Defaults are still tuned for 5 ms sampling (too sensitive at 2 ms). The owner's values are tuned with `tools/Tune` on four 2 ms
  recordings.
- VoiDPlugins VMulti side note: each button write re-sends the last relative delta (a tiny cursor nudge per click). That is the
  output plugin's behaviour, not ours.
