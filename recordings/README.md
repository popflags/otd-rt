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
| `mixed-strokes-taps-500hz-20261014.csv` | 116 s on the 2026-10-14 firmware (~1000 reports/s, a new pressure sample every 2nd report = 2.004 ms, repeated on the next). Unlabelled mixed use: 331 strokes (median 86 ms, 5 over 1 s, up to 21 s), peaks around 4000. One phantom 8191 sample right before a contact. Host timing only (no byte-17 counter column); reports arrive in bursts, so consecutive samples are 0.2–4 ms apart by `t_ms`. |
| `mixed-lines-taps-500hz-20261014.csv` | 118 s, same firmware and format: 267 strokes (median 75 ms, 12 over 1 s), 17 of them one- or two-sample phantom 8191 contacts. Unlabelled. |
| `mixed-long-drags-500hz-20261014.csv` | 174 s (108 s of it pen out of range), same firmware and format: 47 strokes, 7 over 1 s (up to 12.5 s), 8 phantom 8191 contacts. Unlabelled, so cut-out counts are not meaningful; mostly long drags. |
| `play-ptk670-5ms-20261005.csv` | 84 s of play after a firmware change: pressure sampled every 5 ms (1000 reports/s). 106 strokes, all lifted (~93 taps, 13 holds up to 8191), 2 phantom 8191 samples right before contacts, 1 landing-wobble re-press. Recorded with 2.4.0 defaults. |
| `play-ptk670-newtip-20261001.csv` | 231 s of S4 League with another nib, recorded with 2.3.0: 231 strokes (175 taps, 55 holds), 8 phantom one-sample 8191 contacts after lifts, 12 re-presses inside strokes. Same 9 ms pressure sampling. |
| `play-ptk670-20261001.csv` | 108 s of play, recorded with the defaults of the time: 103 strokes, all lifted (≈70 taps of 10–210 ms, ≈32 holds of 320–810 ms at 6000–8191). Pressure changes every ~9 ms (1000 reports/s). No drags longer than 0.8 s, no re-presses without lifting. |
3. Copy the files here and commit them (from a cloud session: upload/paste them, or push from any machine).

Calibrate with every drag file and every tap file:

```sh
dotnet tools/RtTool/bin/Release/net8.0/rt.dll calibrate $(printf -- '--drag %s ' recordings/drag-*.csv) $(printf -- '--tap %s ' recordings/tap-*.csv)
```
