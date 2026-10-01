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
| `play-ptk670-20261001.csv` | 108 s of play, recorded with the defaults of the time: 103 strokes, all lifted (≈70 taps of 10–210 ms, ≈32 holds of 320–810 ms at 6000–8191). Pressure changes every ~9 ms (1000 reports/s). No drags longer than 0.8 s, no re-presses without lifting. |
3. Copy the files here and commit them (from a cloud session: upload/paste them, or push from any machine).

Calibrate with every drag file and every tap file:

```sh
dotnet tools/RtTool/bin/Release/net8.0/rt.dll calibrate $(printf -- '--drag %s ' recordings/drag-*.csv) $(printf -- '--tap %s ' recordings/tap-*.csv)
```
