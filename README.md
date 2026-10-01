# Rapid Trigger for OpenTabletDriver

Rapid trigger for the pen tip on OpenTabletDriver 0.6.x (built against 0.6.7), tuned for high report rate tablets
(developed on a Wacom PTK-670 sending 1000 reports/s, with a new pressure sample every ~9 ms). Taps press and release as early as the pressure
signal allows, while drags stay held through normal pressure wobble.

> Rapid trigger can trip anti-cheats or break game rules. Use at your own risk.

## How it works

Every tablet report goes through a small state machine (`src/RapidTrigger/TriggerEngine.cs`).

**Press**
- *Contact*: from a lifted pen, the first report at or above **Contact Threshold** presses. Nothing
  is predicted or filtered, so the first press is the first report that shows contact.
- *Re-press without lifting*: pressure rising **Activation Distance** above its lowest point since
  the last release.

**Release**: two detectors run side by side, and whichever fires first releases.
- *Fast-fall detector (CUSUM)*: each new pressure sample adds its fall minus what a drag could fall
  since the previous sample (**Fast Fall Speed** × elapsed ms). Reports that repeat the last pressure
  value are not new samples: the PTK-670 on 1000 Hz firmware repeats each value for ~9 reports. Rises and slow falls drain it back to zero. It
  releases once the excess reaches **Fast Release Distance**. Drag dips are slower than Fast Fall
  Speed, so they never add up. A real release starts counting the moment it gets faster than any
  drag, with no smoothing lag. This is Page's CUSUM change detector, the standard quickest-detection
  method for a change in slope.
- *Slow path*: falling **Release Distance** below a hold reference. The reference jumps to new peaks
  instantly and drifts toward the current pressure with **Drift Time Constant**. Slow easing off
  during a drag is absorbed, while deliberate releases that are slower than Fast Fall Speed are still
  caught. The reference stops drifting while a fast fall is building up.
- *Lift*: pressure at or below **Lift Threshold** always releases.

While pressed, the plugin reports **full pressure** (unless *Preserve Pressure* is on). The tip
binding therefore fires on the very first pressed report, whatever the tip threshold in the Bindings
tab is set to.

The filter runs pre-transform and does no allocation or I/O on the report thread. Diagnostics are
written by a background thread.

### Compared with the previous attempts (`otd-rt-old`)

| Old approach | Why this differs |
|---|---|
| Peak + clamped ratio (0112) | Kept as the slow path, but with a drifting reference so slow drag dips don't add up. The fast detector releases taps earlier than a fixed distance can. |
| Trailing peak × decay factor (ratio, valley, 1229–1231, async) | Those decayed toward **zero** by a per-report factor. The strength depended on absolute pressure, and changed 3.3× when going from ~300 Hz to 1000 Hz. Here the reference decays toward the **current pressure** with a time constant in ms, measures sudden drops in full, and freezes during fast falls. |
| Velocity / acceleration / jerk / predictive (Velo, Accel, Pred*, Kalman) | Those triggered on a smoothed derivative alone, which lags and fires on wobble regardless of how far the pen fell. CUSUM needs both speed above the drag allowance *and* an accumulated amount of fall, using raw per-report differences. |
| Z-score / self-tuning classifiers (ZRobust, SD, HSK, sg_auto) | Thresholds learned online drift with use. Here thresholds are fixed and calibrated offline from your own recordings, so behaviour is repeatable. |
| Synchronous file logging | Logging now goes through a lock-free channel to a background writer. |

## Install

Download `RapidTrigger.zip` from the
[latest release](https://github.com/popflags/otd-rt/releases/latest), or a test build from any CI run
(Actions → run → Artifacts). Extract it into the plugins folder:

- Linux: `~/.config/OpenTabletDriver/Plugins/`
- Windows: `%localappdata%\OpenTabletDriver\Plugins\`

To build it yourself, you need the .NET 8 SDK:

```sh
./build.sh
cp -r dist/RapidTrigger ~/.config/OpenTabletDriver/Plugins/
```

Restart OpenTabletDriver. Then, in the Filters tab:

1. Remove older Rapid Trigger plugin versions.
2. Enable **Rapid Trigger** and put it first in the filter list, ahead of any smoothing or
   interpolation filter.
3. In the Bindings tab, set the **pen tip threshold to 0%**. On 0.6.7 the plugin's full-pressure output
   works with any threshold, but upcoming OpenTabletDriver versions apply the threshold *before* all
   filters. A non-zero threshold would then delay the first press and distort the distances.
4. Leave *Preserve Pressure* off for games.

## Tuning: record, then calibrate

Defaults are a starting point. They come from a 108 s gameplay recording on the PTK-670 (taps and
~600 ms holds, `recordings/`), an old 300 Hz log and synthetic drags and taps; your hand, nib and
firmware decide the real numbers. If `rt replay` shows pressure changing every report (true 1000 Hz
pressure sampling), sensor noise adds up in the fast detector: raise Fast Release Distance to ≥ 40.

1. Enable **Enable Diagnostics**. Each time the filter is applied, it writes
   `~/rapid-trigger-logs/rt-<time>.csv` (Windows: `C:\Users\<you>\rapid-trigger-logs\`).
   Commit recordings to `recordings/` (see its README); CI replays them on every push.
2. Record a **drag session**: 1–2 minutes of the drags you do in game (slow, fast, circles, long
   holds, light and heavy pressure). Lift only at the end of each drag. Restart the daemon (or toggle
   the filter) to start a new file.
3. Record a **tap session**: streams, bursts and single taps the way you play.
4. Run:

   ```sh
   dotnet tools/RtTool/bin/Release/net8.0/rt.dll calibrate \
       --drag ~/rapid-trigger-logs/rt-drag.csv --tap ~/rapid-trigger-logs/rt-tap.csv
   ```

   For each drift time constant / fast fall speed pair, it finds the smallest release distances
   with **zero cut-outs** across all drag logs, adds a margin (`--margin`, default 35%), and
   reports how fast taps release and whether any were missed. Then it prints the recommended
   settings.
5. Enter the settings and turn diagnostics off.

`rt replay <log> [--set Name=Value ...] [--events]` runs any settings over a log. It prints
the pressure sample interval, presses, releases (fast / distance / lift), release timing from the
start of each descent, release lead before the lift (larger = earlier; for strokes that end in a lift),
cut-outs (meaningful for drag logs, and for any log where every stroke is one press), missed taps
(meaningful for tap logs) and a sensor-noise estimate. Old `Timestamp,X,Y,Pressure` logs are accepted too.

## Settings

| Setting | Default | |
|---|---|---|
| Contact Threshold | 4 | First press from the air. Lower = earlier. |
| Lift Threshold | 2 | Always released at or below this. |
| Activation Distance | 40 | Re-press rise above the trough. Keep ≥ 6× noise sigma (`rt replay` prints it). |
| Fast Fall Speed | 20 raw/ms | Fastest fall a drag produces. Higher = steadier drags, later taps. 0 = fast detector off. |
| Fast Release Distance | 20 | Excess fall that releases. Lower = earlier taps. ≥ 40 if pressure is really sampled every ms. |
| Release Distance | 600 | Slow-path fall from the hold reference. |
| Release Ratio / Max Release Distance | 0 / 1000 | Optional proportional slow-path distance. |
| Drift Time Constant | 10 ms | Slow-path drift. 0 = classic peak rapid trigger. |
| Press Drift Time Constant | 0 ms | Ignores slow creep while released. 0 = off. |
| Hold Time / Hold Release Multiplier | 150 ms / 1 | Scale both release distances up for long presses. 1 = off. |
| Preserve Pressure | off | Pass real pressure instead of full pressure while pressed. |
| Enable Diagnostics / Diagnostics Directory | off / `~/rapid-trigger-logs` | Per-report CSV for replay and calibration. |

## Development

```
src/RapidTrigger/         plugin (TriggerEngine.cs has no OpenTabletDriver dependency)
tools/RtTool/             replay + calibration CLI, compiles the same TriggerEngine.cs
tests/RapidTrigger.Tests/ engine tests on synthetic 1000 Hz signals
tools/synth_logs.py       synthetic drag/tap logs for experiments
recordings/               real recordings for replay and calibration
```

`RtTool.zip` (in releases and CI artifacts) runs on any machine with the .NET 8 runtime:
`dotnet rt.dll --help`. `CLAUDE.md` holds the design history and measurements for AI-assisted
development; cloud sessions install the SDK through `scripts/cloud-setup.sh`.

`dotnet test tests/RapidTrigger.Tests` runs the tests.
