# Handoff: state, locations, procedures (updated 2026-10-20 night, 2.14.1)

Read this first after a context reset, then `CLAUDE.md` (design history and evidence) and the firmware session's notes
`ptk670-firmware-updates.md` / `ptk670-logs.md` / `ptk670-analog-pen.md` in the repo root (untracked, written by the firmware session). Analysis scripts are in
`scratch/` (see `scratch/README.md`).

## Current state

| What | State |
|---|---|
| Plugin installed | **2.14.1** (`b23ec6a`) on Linux and Windows (same DLL, sha256 `6529f47ad80a8fd6…`), 2026-10-20 ~21:30; Windows loads it at the next OTD start, Linux daemon restarted (loaded, settings applied). Backups `RapidTrigger-2.14.0.dll.bak` and `-2.13.0.dll.bak` (Linux `~/.config/OpenTabletDriver/`, Windows `Backup/`). |
| Rapid Trigger enabled? | **Windows: on**, PTK profile = the cp923 preset (see below). **Linux: on again** (the owner re-enabled it after 15:28): PTK profile Activation 1000, Analog Pen on, Hold Time 400 (not changed by me; the owner asked for the Windows preset only). CTC-6110WL profiles: the owner experiments there; leave them. |
| Git | Branch `fw-0fc2521a`. GitHub `main` = `cb2474b` (release v2.10.0 published). Local, **not pushed**: everything from 2.11.0 to `b23ec6a` 2.14.1 plus handoff commits. `git push origin fw-0fc2521a:main` publishes v2.14.1 only (release.yml tags the current `<Version>`; 2.11–2.14.0 notes would not get their own releases). Untracked in the repo root, not ours: `propen3.json` (a Linux preset export from the owner) and the firmware session's `ptk670-*.md`. |
| Owner's staged files | `recordings/rt-20261014-213607_drag.csv` and `…213849_tap.csv` are staged by the owner and must stay out of commits: always commit with an explicit path list (`git commit -- path…`), never a bare `git commit` / `--amend`. |
| Tablet firmware | Production ring500 **9f4a1b39** (2026-10-20; a12b3929 + analog-pen work only). Pro Pen 3: 1000 Hz, motion sync ON, a pressure sample every 2.000 ms, repeated on the next report; lone 8191 "phantom" readings after lifts remain (4–10 % of lifts). The owner also has the analog **CP923** pen: a sample every report (1 ms), side-button changes step the pressure; tick **Analog Pen** for it (2.14.0). |
| Game | S4 League on a private server that reads input at 3000 ticks/s (click length vs frame rate is not an issue). |
| Output | Linux: Relative Mode + MouseBinding Left. Windows: VMulti Relative Mode + VMultiButtonHandler Left. Tip threshold 0 % on both. Keep them different. |
| Other enabled PTK filters | Hover Distance Limiter (PostTransform, 0–255, Near Proximity cutoff on), Scroll Binding (pass-through), Saturn HRRNC (position only). Decompiled 2026-10-20: none drops reports or changes pressure; VMulti writes every button change at once. |
| Diagnostics | Off on both (the owner turned Windows off after the 2026-10-20 session). Windows writes to `C:\Users\Administrator\rapid-trigger-logs\` (~60 MB per 20 min). Since 2.12.0 the CSV has a `detected` column; `pressed` / `event` are the output after the Touchdown Gate. Since 2.14.0 also `buttons` (side buttons) and `gap` (analog clock added a report gap). |

### Owner's Rapid Trigger settings

**Windows presets** (`…/OpenTabletDriver/Presets/`, full settings snapshots the owner switches between in the OTD UI; the live
`settings.json` currently equals cp923): **cp923.json** and **propen3.json**, both with **Activation Distance 1000** (the owner's
"1k activation point route", 2026-10-20 night; re-presses only on a 1000 push), differing in **Analog Pen** (on / off) and **Hold
Time** (cp923 **300** since 2.14.1, propen3 400). `scratch/owner-settings-cp923.args` / `owner-settings.args` hold them as `--set`
lines (generated from the preset files). Backups `Backup/cp923.json.before-2.14.1`, `settings.json.before-2.14.1`.

Before that: no-re-press mode (Activation 10999 / Linux 100000). Common values: **Hold Allowance 4**, **Release Distance 1000**,
**Rebound Distance 10**, Glitch Jump 0, Dropout Time 0, Button Settle Reports 10, Firmware Sample Clock on. Rest: Contact Threshold 4 · Phantom Contact Pressure 8191 · Phantom Confirm Time 20 · Dropout Pressure 8190 · Lift
Threshold 2 · Activation Percent 0 · Fast Fall Speed 12 · Fast Fall Percent 0.2 · Fast Release
Distance 35 · Tap Allowance 0.2 · Re-press Allowance 0.3 · Press Holdoff 12 · Minimum Click 0 · Touchdown Gate 0 · Release Ratio 0
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

## Procedures

**Tooling after a reboot.** `scratch/setup.sh` (installs the .NET 8 SDK and ILSpy under `~/.cache/otd-rt-tools`).
The system `dotnet` cannot build. The tool shell is zsh: `$VAR` holding spaces does not split, so wrap loops in `bash -c '…'`.
Do not `cd` into the Windows drive in a shell command.

**Writing to the Windows partition.** No pre-checks (owner, 2026-10-20: "delete the check ntfs state rule entirely, ill see
when the problem is there"). Mount it and write; keep writes to the DLL and `settings.json`.

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
| 2.13.0 | Hold Allowance, Rebound Distance (both off by default) | the owner's no-re-press mode (Activation Distance huge) cut drags: drag-end dips that hold level and landing dips release for good; 69 of 72 drag-end cut-outs gone at Hold Allowance 4 + Release Distance 1000 (drag ends release ~20 ms later) |
| 2.12.0 | Touchdown Gate (experimental): a touchdown clicks no sooner than N ms (500) after the previous touchdown's click | owner: "wont allow fast taps to go actuate below 500ms"; blanket over taps and drags (a tap-only gate is not possible from pressure, and the owner ruled out x/y) |
| 2.14.1 | Analog Pen: a report gap the clock sees also starts the settle window; a contact ends it; cp923 preset Hold Time 300 | first CP923 gameplay: a button 1 pre-fall after a gap cut a hold (Activation 1000 kept it up); a hold wobble at 179 ms (margin 0.89 → 1.20) |
| 2.14.0 | Analog Pen toggle (CP923): 1 ms sample clock + report-gap rule, side-button pressure steps rebased for Button Settle Reports (10), phantom check off; Mismatch resets on a pen change; diagnostics `buttons` / `gap` columns, `td2csv.py --analog` | owner: "build a toggle based of ptk670-analog-pen.md"; the plan's one-report rebase left ~80 % of the button-change releases (the firmware ramps for ~10 reports) |

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
- **Python `open(f, 'w').write(open(f).read()…)`.** It truncates the file before reading it, which emptied `tools/Tune/Program.cs`
  once; it was restored from git.

## Open items

- **CP923 / Analog Pen, what is left** (CLAUDE.md evidence 2026-10-20, `recordings/play-cp923-20261020.csv`):
  - **For the firmware session:** (1) a one-report dip of 50–230 raw when a press rises through ~800 raw (all 28 such bounces in three
    CP923 logs start at 767–805; `scratch/bounce_dips.py`); it released 2 landings in the first gameplay session and no setting
    absorbs it cheaply. (2) Side-button re-tunes: a 3–5 ms report gap, then (button 1 press) ~110 per report of pre-fall for 3–6
    reports, then the bits and a ~10-report ramp/overshoot. 2.14.1 covers this from the gap on (live Windows timing is clean), but
    holding the pressure through the re-tune in firmware would make the settle window unnecessary.
  - The gap trigger relies on live timing like Windows (6 touching gaps in 238 s, all re-tunes). Linux live timing with the CP923 is
    unmeasured (only debugger logs, which are bursty); a Linux diagnostics session would show whether gaps fire away from re-tunes.
  - The mismatch warnings at 20:55:39 / 20:58:22 (Windows log) came after the cp923 settings were applied with another pen in
    use (probably the Pro Pen 3 with Analog Pen on): expected fallback; the propen3 preset is the right one for that pen.
  - Optional later: Off / On / Auto from the tool bytes (CP923 `00 00 00 00 02`, Pro Pen 3 `3E C9 58 26 00`).

- **No-re-press mode, what is left** (`scratch/norearm_cuts.py`, `scratch/repress_modes.py`): on ~47 min of gameplay 3 drag-end and
  30 landing cut-outs remain (most landing ones are taps with a two-stage shape; 8 are drags that start light, nearly lift and then
  press hard, lost entirely with Activation 10999). Activation Distance 1000 instead recovers them (49 re-presses on big pushes, 1
  wobble click; button up while pressing 1.5 s total vs 6.6 s with 10999). Offered to the owner, not set. Next live session with
  diagnostics on would show which cut-outs the owner actually feels.

- **"No re-press until the pen lifts" (asked 2026-10-20, not implemented):** `scratch/no_rearm.py` with `scratch/owner-settings.args`.
  On the current-firmware gameplay logs (game, game2, a, b, the three 2026-10-20 diagnostics; ~47 min, 2019 touchdowns) it loses 140
  re-presses: 88 progress ≥ 100 raw past the activation point (60 at drag ends = drag into tap, 25 in a touch's first 100 ms, 11 of
  them holds that would shrink to a ~20 ms click; 3 mid-touch), 52 barely pass it (< 100 raw, clicks ~20 ms: two-stage lifts and
  landing wobbles, which would disappear). No trough came within 50 of zero, so a higher "lifted" level saves none.

- **Touchdown Gate (2.12.0), now 0.** Live sessions with 400: `rt-20261020-014808.csv` / `-015015.csv` (the second also with Hold Time 0),
  on Windows only, not committed (6 / 14 MB). Clicks never closer than ~399 ms, 35 / 57 held (p50 ~175 ms), 22 / 44 clicks sent after the
  pen had lifted, the timer fired 5 / 2 times; replay with the same settings reproduces the live output (390 / 390, 784 / 786: the other
  2 differ by one report from CSV time rounding). Findings from the analysis, if it comes back (`scratch/patterns.py`, CLAUDE.md evidence):
  - tap→drag lands a median 432 ms after the tap, so ~80 % of those drags start ~100 ms later with the gate;
  - a rhythm slightly under 500 falls behind (one 27-tap run reached 366 ms behind the hand);
  - fast bursts (150–210 ms) are thinned to one click per 500 ms (7 merged on game2);
  - 13 of 58 drag-end re-presses come from rebounds < 100 raw (probably unintended extra clicks; untouched).
  
  Options if the owner wants them: a lower gate (480: 204 held, p50 81 ms; 450: 160, p50 53), or counting from the previous
  *touch* instead of the previous click (no falling behind, but clicks can then be < 500 apart).

- Push `fw-0fc2521a` to `main` when the owner says so (v2.14.1 release).
- Phantom guard: a real landing at exactly 8191 presses on the first sample below it (4 ms, ~1 in 900 taps). Pressing on a lone 8191
  would turn real phantoms (pen 15–20 above the surface) into clicks. Hover distance would separate them, but the engine is
  pressure-only.
- On a12b3929, Glitch Jump and the dropout guard measured zero cost. They are kept as insurance against older firmware.
- Defaults are still tuned for 5 ms sampling (too sensitive at 2 ms). The owner's values are tuned with `tools/Tune` on four 2 ms
  recordings.
- VoiDPlugins VMulti side note: each button write re-sends the last relative delta (a tiny cursor nudge per click). That is the
  output plugin's behaviour, not ours.
