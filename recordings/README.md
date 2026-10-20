# Recordings

Real diagnostics CSVs from the tablet, used by `rt replay` / `rt calibrate` and replayed by CI on every push.

1. In OpenTabletDriver, enable **Enable Diagnostics** in the Rapid Trigger filter. Files go to
   `~/rapid-trigger-logs/` (Windows: `C:\Users\<you>\rapid-trigger-logs\`). A new file starts each time the
   filter is (re)applied.
2. Record one kind of motion per file and name it after its kind:
   - `drag-<what>-<date>.csv`: drags only. The tip stays down for each whole drag and lifts only at the end.
     Any release before that lift counts as a cut-out.
   - `tap-<what>-<date>.csv`: taps/streams the way you play.
   - `play-<what>-<date>.csv`: gameplay where every stroke is one press that ends with the pen lifted (taps and
     holds). Any release before the lift counts as a cut-out, and `rt replay` prints how long before the lift each
     release came.

| File | Content |
|---|---|
| `play-ptk670-2ms-a-20261019.csv` | 145 s of gameplay (Windows, firmware ring500 83a52e3c at 1000 Hz motion sync ON: a new pressure sample every 2.000 ms, repeated on the next report), converted from OTD Tablet Debugger log `tablet-data_1792424513.txt` with `tools/td2csv.py`: `engine_ms` is the Firmware Sample Clock's time (byte-17 counter). 59 touches: taps of ~60–100 ms and holds of 500–750 ms at 6000–8191 while aiming (20–95 mm of movement), 6 one-sample phantom 8191 contacts. Every contact's first sample is already ≥ 4. 3 slow drag dips (≤ 8 raw/ms), no re-click. |
| `play-ptk670-2ms-b-20261019.csv` | 250 s, same setup (`tablet-data_1792425112.txt`): 126 touches, 5 phantom 8191 contacts. Contains 6 intended re-clicks without lifting during holds (falls of 960–3200 at up to 30–65 raw/ms, rebound 220–960; troughs at engine_ms 42938, 82850, 114196, 130048, 150770, 164700) and 5 slow drag dips (≤ 14 raw/ms) that must not release. Labels and scoring: `tools/Tune`. |
| `play-ptk670-2ms-game-20261019.csv` | 339 s of a real S4 League session, same setup (`tablet-data_1792433577.txt`, raw log git-ignored), recorded with 2.9.0. 191 touches (~100 taps under 100 ms, 53 holds of 0.5–0.9 s, often saturated at 8191), lifts between taps as short as 4 ms. The firmware sends **one- or two-sample glitches while pressed hard** (88: 8191 → 692 → 8191, 7424 → 4496 → 7380, 8117 → 0 → 8113, 7818 → 8191 → 0 → 7802; hover distance stays at the touching value), which cut drags and left a hold released at 130.3 s on 2.9.0; handled by Glitch Jump since 2.10.0. 11 one-sample phantom 8191 contacts after lifts. Labelled re-clicks / slow drag dips in `tools/Tune`. |
| `play-ptk670-2ms-rhythm-20261020.csv` | Live diagnostics from Windows (a12b3929, plugin 2.11.0 with the owner's settings): two windows (170–230 s and 385–440 s of `rt-20261020-002836.csv`) with rhythmic tapping at ~500–540 ms intervals, 177 contacts. `pressed` / `event` are the live plugin output; includes the real contact 2 ms after a phantom that 2.11.1 now presses on its first sample. |
| `play-ptk670-2ms-game2-20261019.csv` | 260 s of S4 League on candidate firmware ring500 a12b3929 (hard-press glitches fixed at the source) and 2.10.0 with the owner's settings (`tablet-data_1792438493.txt`, raw log git-ignored). 163 touches (~100 quick taps of 14–200 ms, 53 holds of 0.5–0.9 s), 3 one-sample phantom 8191 contacts, 1 glitch. Every real tap pressed on its first sample, but 26 clicks lasted under 17 ms (12 ms = Press Holdoff), which a game reading the button once per frame can miss: the basis for Minimum Click (2.11.0). One re-click without lifting at 127.8 s. |
| `play-cp923-20261020.csv` | First **CP923 gameplay** session (Windows, 9f4a1b39, plugin 2.14.0 with Activation Distance 1000 + Analog Pen + the owner's other values), `tools/td2csv.py --analog` of `tablet-data_1792518028.txt` (Windows Deltas are tight, so `gap` marks real re-tunes and range entries). 290 s, 140 touches (70 under 100 ms, 58 drags of 250–1000 ms), few side-button changes. The live diagnostics of the same session (`rt-20261020-204040` / `-204438`, not committed) replay identically. Times by `t_ms`: the button 1 pre-fall at 146.5 s (134.65 s in the diagnostics) that 2.14.1 holds, two landing releases on the ~800-raw firmware dip (13.88 s, 145.78 s) and a hold wobble released 179 ms into a hold at 62.6 s (engine time 24.79 s; Hold Time 300 holds it). |
| `mixed-cp923-buttons-20261020.csv` | 214 s with the **analog CP923 pen** (Linux, production firmware ring500 9f4a1b39), converted with `tools/td2csv.py --analog --no-gap` from `tablet-data_1792513436.txt` (firmware repo): a new pressure sample on every report, `engine_ms` = 1.000 ms per counter step plus the report gaps of side-button changes, `buttons` = status byte 2 bits 1-3. Drawing with ~430 side-button changes (170 while touching, button tests at 141–170 s), 165 touches, no phantoms. Replay with `--set AnalogPen=true`; `scratch/button_releases.py` / `button_steps.py` show what the button changes do. `--no-gap`: the Linux debugger's Deltas are bursty and would fake report gaps, so `gap` is 0 and `engine_ms` is the counter's time. |
| `mixed-cp923-buttons-e9a2e0a9-20261020.csv` | 202 s, CP923 on firmware e9a2e0a9 (same pressure path), `tablet-data_1792508532.txt`: 67 touches, 288 side-button changes while touching. Same format. |
| `mixed-strokes-taps-500hz-20261014.csv` | 116 s on the 2026-10-14 firmware (~1000 reports/s, a new pressure sample every 2nd report = 2.004 ms, repeated on the next). Unlabelled mixed use: 331 strokes (median 86 ms, 5 over 1 s, up to 21 s), peaks around 4000. One phantom 8191 sample right before a contact. Host timing only (no byte-17 counter column); reports arrive in bursts, so consecutive samples are 0.2–4 ms apart by `t_ms`. |
| `mixed-lines-taps-500hz-20261014.csv` | 118 s, same firmware and format: 267 strokes (median 75 ms, 12 over 1 s), 17 of them one- or two-sample phantom 8191 contacts. Unlabelled. |
| `mixed-long-drags-500hz-20261014.csv` | 174 s (108 s of it pen out of range), same firmware and format: 47 strokes, 7 over 1 s (up to 12.5 s), 8 phantom 8191 contacts. Unlabelled, so cut-out counts are not meaningful; mostly long drags. |
| `play-ptk670-5ms-dropouts-20261005.csv` | 90 s of gameplay, 5 ms sampling: 58 contacts, many hard presses that hit the firmware's dropouts (0 for 5–45 ms from 7750+, 8191 / 0 bursts while saturated). Basis for the 2.5.2 dropout guard. |
| `play-ptk670-5ms-20261005.csv` | 84 s of play after a firmware change: pressure sampled every 5 ms (1000 reports/s). 106 strokes, all lifted (~93 taps, 13 holds up to 8191), 2 phantom 8191 samples right before contacts, 1 landing-wobble re-press. Recorded with 2.4.0 defaults. |
| `play-ptk670-newtip-20261001.csv` | 231 s of S4 League with another nib, recorded with 2.3.0: 231 strokes (175 taps, 55 holds), 8 phantom one-sample 8191 contacts after lifts, 12 re-presses inside strokes. Same 9 ms pressure sampling. |
| `play-ptk670-20261001.csv` | 108 s of play, recorded with the defaults of the time: 103 strokes, all lifted (≈70 taps of 10–210 ms, ≈32 holds of 320–810 ms at 6000–8191). Pressure changes every ~9 ms (1000 reports/s). No drags longer than 0.8 s, no re-presses without lifting. |
3. Copy the files here and commit them (from a cloud session: upload/paste them, or push from any machine).

Calibrate with every drag file and every tap file:

```sh
dotnet tools/RtTool/bin/Release/net8.0/rt.dll calibrate $(printf -- '--drag %s ' recordings/drag-*.csv) $(printf -- '--tap %s ' recordings/tap-*.csv)
```
