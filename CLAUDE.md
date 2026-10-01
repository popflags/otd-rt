# Rapid Trigger for OpenTabletDriver: notes for Claude

Read this before changing the trigger logic. It records what was tried, measured and decided, including
material that only exists on the owner's Linux machine and is git-ignored (`otd-rt-old/`,
`TheSaturnCollection-main/`, `logs/`). The cloud session does not have those folders; everything
useful from them is summarized below.

## Owner and goal

- Owner (GitHub `popflags`) plays rhythm/aim games with a **Wacom PTK-670 (Intuos Pro M, 2025)** running
  **custom firmware at 1000 Hz**, on OpenTabletDriver (OTD) **0.6.x**. Develops on Linux (CachyOS) locally
  and on Windows through Claude Code in the cloud (no IDE on Windows).
- Requirements, in the owner's words: initial press as fast as possible, release as fast as possible,
  taps as fast as possible, "compromise as little as possible". **Drags must never cut out** (tip held
  down while moving must not release and re-press mid-drag) in normal use. Both early releases and
  delays are unacceptable.
- The owner iterated through ~25 discarded variants before this rewrite (see below). Do not quietly
  re-implement one of those; if an idea overlaps, say so and explain what is different.

## Repository layout

```
src/RapidTrigger/TriggerEngine.cs        the whole algorithm, no OTD dependency, allocation-free
src/RapidTrigger/RapidTriggerFilter.cs   OTD 0.6 filter: properties, timing, output pressure, diagnostics
src/RapidTrigger/DiagnosticsRecorder.cs  per-report CSV via Channel + background thread
tools/RtTool/                            `rt` CLI: replay + calibrate; compiles TriggerEngine.cs via <Compile Link>
tools/synth_logs.py                      regenerates syn_drag.csv / syn_tap.csv (synthetic 1000 Hz data)
tests/RapidTrigger.Tests/                xunit tests on synthetic 1000 Hz signals (Signals.cs)
recordings/                              real diagnostics CSVs from the tablet (commit them here)
scripts/cloud-setup.sh                   SessionStart hook: installs the .NET 8 SDK in cloud sessions
.github/workflows/ci.yml                 build + test + replay recordings on every push, uploads DLL artifact
.github/workflows/release.yml            on tag v*: build + GitHub release with RapidTrigger.zip/.dll + RtTool.zip
```

## Commands

```sh
./build.sh                                         # test + build + dist/RapidTrigger.zip + dist/RtTool.zip
dotnet test tests/RapidTrigger.Tests
dotnet tools/RtTool/bin/Release/net8.0/rt.dll replay <csv>... [--set Name=Value]... [--events]
dotnet tools/RtTool/bin/Release/net8.0/rt.dll calibrate --drag <csv>... --tap <csv>... [--margin 0.35]
python3 tools/synth_logs.py /tmp/syn                 # synthetic logs for quick experiments
```

In cloud sessions `scripts/cloud-setup.sh` installs the SDK (apt `dotnet-sdk-8.0`, falling back to
dotnet-install.sh into `~/.dotnet`). If `dotnet` is missing, run `scripts/cloud-setup.sh --force` and
use `~/.dotnet/dotnet`. Shell note for the owner's local machine: the login shell is fish and the tool
shell is zsh. `$VAR` with spaces doesn't word-split there, so wrap loops in `bash -c '...'`.

### Testing on Windows without building

Every push runs CI and uploads `RapidTrigger-<sha>` (RapidTrigger.zip + RtTool.zip) as an artifact on
the Actions run page. The owner extracts RapidTrigger.zip into `%localappdata%\OpenTabletDriver\Plugins\`
and restarts OTD. Releases: bump `<Version>` in `src/RapidTrigger/RapidTrigger.csproj`, then
`git tag -a vX.Y.Z -m ... && git push origin vX.Y.Z`. Commit messages end with the Co-Authored-By trailer.

## OpenTabletDriver 0.6.6.2 facts (verified in its source)

- Plugin API: `IPositionedPipelineElement<IDeviceReport>` with `Consume`, `event Emit`, `Position`.
  Attributes `PluginName`, `Property`, `BooleanProperty(name, desc)`, `DefaultPropertyValue`, `ToolTip`,
  `Unit`; `[TabletReference] public TabletReference X { set; }` is injected at construction.
  NuGet `OpenTabletDriver.Plugin` 0.6.6.2, referenced with `ExcludeAssets="runtime"`.
- Filters are constructed on every settings apply (properties set before the first `Consume`).
  The output mode disposes `IDisposable` elements.
- Pipeline: PreTransform elements → transform → PostTransform elements → output, in profile list order.
  `BindingHandler` is **appended last** as a PostTransform element.
- **Tip binding** (`ThresholdBindingState`): `pressed = pressure% > TipActivationThreshold` (strictly
  greater), then pressure is remapped above the threshold. Consequence: if a filter passes raw pressure
  and the user's tip threshold is above 0%, presses are delayed until raw pressure exceeds it. That is
  why the plugin reports **full MaxPressure while pressed** by default (Preserve Pressure off).
- Out-of-range: `OutOfRangeReport` (struct, namespace `OpenTabletDriver.Plugin.Tablet`). The binding
  handler only releases *pen buttons* on it, not the tip.
- PTK-670 config: `MaxPressure` 8191, digitizer 52600×29600, parser
  `OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV3.IntuosV3ReportParser`. `0x1F` reports with
  `data[1]==0x01` → `IntuosV3Report` (a **struct**: `ITabletReport, IProximityReport, ITiltReport,
  IEraserReport`, pressure = ushort at byte 7, `HoverDistance` byte 13). Other `0x1F` reports, i.e. out of
  proximity, become plain `DeviceReport`, not `OutOfRangeReport`. Mutating `Pressure` through the
  `ITabletReport` interface changes the boxed report in place, which is how the filter works.
- There is no timestamp in reports; the filter times reports with `Stopwatch` at `Consume`, capping gaps at 50 ms.

## The algorithm (TriggerEngine)

Per report: `Update(rawPressure, elapsedMs)`.

- **Released:** the trough tracks the minimum (optional `PressDriftTimeConstant` lets it creep up).
  - *Fresh contact* (pressure hit ≤ `LiftThreshold` since the last release): press on the first report
    ≥ `ContactThreshold`.
  - Otherwise *Rearm*: press when pressure ≥ trough + `ActivationDistance`.
- **Pressed:** whichever detector fires first releases.
  1. **Lift:** pressure ≤ `LiftThreshold`.
  2. **FastRelease (CUSUM):** `FallExcess = max(0, FallExcess + (prev − p) − FastFallSpeed·dt)`; release
     when ≥ `FastReleaseDistance` (× hold multiplier). Only fall faster than the drag allowance accumulates.
     There's no smoothing, so it reacts on the first fast report.
  3. **Release (slow path):** hold reference − p ≥ `ReleaseDistance` (or `ReleaseRatio`·ref, capped by
     `MaxReleaseDistance`, × hold multiplier). The hold reference jumps to new peaks and drifts toward the
     **previous** sample with `DriftTimeConstant`, so a sudden drop is measured in full. It does not drift
     while `FallExcess > 0`.
- Defaults: Contact 4, Lift 2, Activation 40, FastFallSpeed 20 raw/ms, FastReleaseDistance 40,
  ReleaseDistance 600, ReleaseRatio 0, Max 1000, Drift 10 ms, PressDrift 0, HoldTime 150, HoldMult 1.

### Evidence behind the design and defaults

The only real data so far is the old **~300 Hz** log (`logs/pressure_log_detailed.csv`, see below). No
1000 Hz recordings exist yet, and getting them is the most valuable next step. Numbers from `rt replay`:

| | old 0112 settings | defaults |
|---|---|---|
| synthetic drags (`syn_drag`): cut-outs | 60 | 0 |
| synthetic taps (`syn_tap`): release p50 / pressure shed | 7 ms / 447 | 3 ms / 132 |
| real 300 Hz log: missed taps (of 116 estimated) | 0 | 6 |
| real 300 Hz log: release p50 from descent start | 29 ms | 33 ms |

Old 0112 settings for replay: `--set ContactThreshold=4 --set LiftThreshold=9 --set ActivationDistance=4
--set ReleaseDistance=10 --set ReleaseRatio=0.2 --set MaxReleaseDistance=400 --set DriftTimeConstant=0
--set FastFallSpeed=0`.

Findings that shaped it:
- In the real log, slow pressure dips during sustained high-pressure holds reach **2000–3100 units
  (25–38%) over 500–800 ms**, with peak slopes up to ~20–45 units/ms. Intentional releases have a median
  peak slope of ~53 units/ms (5th percentile ~27). No fixed peak distance separates them.
- Real releases start slowly at the peak (rounded): 5430, 5430, 5394, 5234, 4905… every 3 ms. Every
  detector is limited by this. An EMA-velocity gate (tried during this rewrite) added ~3 ms of lag and
  gave no gain, which is why it was replaced by CUSUM.
- In sweeps, a proportional release ratio never beat a fixed distance on the real log, so the default is 0.
- **Unresolved conflict:** no setting gave both 0 missed real taps and 0 synthetic-drag cut-outs. The
  defaults favour drags. The 6 missed real taps are slow, shallow (~35–40%) releases that look like drag
  dips. The synthetic drags (30% dips over 400 ms + 3% tremor at 9 Hz + ±15 noise) were made up and may be
  harsher than real drags. **Real 1000 Hz recordings decide this, via `rt calibrate`.**

### Metrics in RtTool (know their limits)

- *Release timing*: ms from the start of the descent (most recent prominent peak; last sample within
  max(3σ, 1%) of it) to the release, plus pressure shed. On slow falls, "start of descent" is fuzzy.
- *Cut-outs* (drag logs): a Release/FastRelease followed by a re-press before pressure reaches the lift
  threshold, or no lift within `--lift-window` (300 ms). Only meaningful when the log contains drags only.
- *Missed releases* (tap logs): zigzag swings of ≥ max(400, 35% of peak) whose peak was pressed but which
  never released before the trough. A heuristic: on drag logs it counts dips as "taps".
- *Noise σ*: 1.4826·median|2nd difference|/√6 over samples > 50. Calibrate recommends Activation ≥ 6σ,
  slow-path distance ≥ 10σ and fast distance ≥ 4σ.
- The calibrator scales ReleaseDistance, ReleaseRatio, MaxReleaseDistance and FastReleaseDistance together
  (bisection to the smallest scale with zero cut-outs) for each DriftTimeConstant × FastFallSpeed pair,
  adds the margin, and picks the fastest p90 tap release with no missed taps.

## Conventions

- `TriggerEngine.cs` must stay free of OTD references; the tool and tests link the same file, so replays
  match the driver exactly.
- Report thread: no allocation, no I/O, no locks. Diagnostics go through `DiagnosticsRecorder` only.
- All settings are `double`. Time is in ms and pressure in raw units. Keep the plugin properties,
  `TriggerSettings`, the `RapidTriggerFilter.CreateEngine` mapping, the README settings table and the RtTool
  usage text in sync.
- Every behaviour change: add or adjust a test in `TriggerEngineTests`, run `rt replay` on
  `recordings/` and the synthetic logs, and report before/after numbers honestly (including regressions).

## Ideas not yet explored

- Calibrate FastFallSpeed and FastReleaseDistance independently of the slow path (currently scaled together).
- Press side: a CUSUM on rises could allow a smaller ActivationDistance (gain is sub-ms at 1000 Hz).
- `HoverDistance` (IntuosV3 byte 13) could hint at an imminent contact, but any prediction risks false presses.
- Per-pressure-band thresholds if real drags show noise growing with force.

---

## Git-ignored material (exists only on the owner's Linux machine)

### `otd-rt-old/`: the previous plugin and its discarded variants

Forked from Kuuuube/Rapid_Trigger. It contains `.modules/OpenTabletDriver-0.6.x` (OTD 0.6.6.2 source,
what the old csproj referenced) and `.modules/OpenTabletDriver-master`. The variants are full `.cs` files
saved with odd extensions in `otd-rt-old/Rapid_Trigger/`. All were `PostTransform`, output raw pressure
(min 1) while pressed, and counted time in **reports**, not ms. Oldest first:

| File (date) | Plugin name | Logic | Why it fell short (inferred) |
|---|---|---|---|
| `.bak` (2025-11-12) | Rapid Trigger | Kuuuube original: clamped accumulators of +Δ/−Δ pressure; press/release when an accumulator hits the sensitivity (250) | Accumulators fill on slow drift too |
| `.workinglogic` (11-12) | Rapid Trigger | Absolute thresholds with direction check (press ≥ 1 and rising, release ≤ 0 and falling) | Not rapid trigger, only lift-off |
| `.3step` (11-13) | Actuator | Press at ≥ 1; arm once pressure passes 1800; release when falling below 1800 | Fixed absolute level; drags below/around 1800 break |
| `Velo.city` (11-13) | Rapid Trigger (Velocity) | Velocity over an 8-report window; press at v ≥ 150/report, release at v ≤ −150; release gates until pressure is 0 | Window lag; no re-press without lifting; wobble triggers |
| `TimerwThreshold.best` (11-13) | Peak Timer Hybrid | Tapping: release on peak − tap_sensitivity; above hold_threshold for N reports → Holding: release only below an absolute level | Holds become slow to release; report-count timer |
| `Accel.ac` (11-13) | Rapid Trigger (Jerk) | Thresholds on windowed jerk (3rd derivative) | Extremely noisy, lag from 3 cascaded windows |
| `Pred.ac`, `PredV2_IIR.IIR`, `PredVKalman.aids` (11-14) | Predictive / IIR / Kalman | Estimate p, v, a (windows, IIR or 2-state Kalman); release when p + v·t + ½a·t² (t = 3 reports) ≤ floor; press on acceleration | Prediction overshoots on wobble (false releases) and is model-dependent |
| `ZRobust_v1.aaa` (11-14), `zrobust_v2.txt` (11-15) | Pure Pressure Z-Score | Kalman velocity, EMA mean/variance → z-score; press z > 3, release z < −2.5 | Statistics adapt to the motion itself; thresholds drift |
| `SD.txt` (11-15) | Self-Tuning Classifier | Savitzky–Golay (7-pt) velocity/accel, z-score, online-learned tap vs hold release thresholds | Self-tuning drifts, unpredictable |
| `sg_auto.txt` (11-15) | Hunter-Seeker | SG + z-score + self-tuning + "aggressive sensitivity step" | Same family |
| `HS_KALMAN_best.txt` (11-16), `Rapid_Trigger - Copy.txt` (11-17), `Rapid_Trigger.txt` (11-26, "HSK Continuous") | Kalman Hybrid | Kalman + z-score + provisional tap/hold classification by duration (100 reports) + learned thresholds | Complex, still z-score based |
| `flux.txt`, `FLUX_STATIC.txt` (11-26) | Pressure Latch / Static Pro | Kalman velocity; fresh touch presses immediately; re-press on velocity breakout or distance; release on velocity < −threshold (static) or < noise envelope × ratio, with no release while pressure > 2000 (latch) | Velocity-only release fires on fast wobble; latch blocks releases at high pressure |
| `flux_stable.txt` (11-27) | Static + File Log | FLUX_STATIC + synchronous file logging (Velocity Cut −120) | Logs show releases with very little drop ("Velocity Cut (-144 < -60)") |
| `ratio.txt` (12-13) | Trailing Peak | Peak ×0.99 per report when below; release when (peak − p)/peak > 15% and v < −1; panic v < −500; floor | Decay toward **zero** depends on absolute pressure and report rate |
| `semistable_valley.txt` (12-14) | Valley Guard | Trailing peak ratio 10%, decay 0.995, but ignore releases while velocity is in a "valley" band (−150…−500) unless ratio > 60% | Patch for spiral drawing; magic numbers |
| `STABLE_RATIO.txt` (12-15) | Decay + Diagnostics | Kalman (Q=100); trailing peak decay 0.92/report; release ratio 6%; panic −800; synchronous diagnostics log | 0.92/report is effectively a velocity trigger; log (`RapidTrigger_Diagnostic.txt`) shows releases at 6–15% ratio |
| `1229.txt` (12-18) | Ratio Rapid Trigger | Kalman removed; trailing peak decay 0.992; ratio 4%; fresh/distance press | Same decay-toward-zero issue |
| `1230.txt` (12-30) | Anti-Lock Fix | Decay 0.9993; drop = max(ratio 3.2% × peak, 80); air cut < 10; activation 4 | Lowered decay so holds don't "lock" |
| `1231.txt` (12-31) | Dynamic | 1230 + Stopwatch "bounce window": activation 60 for 50 ms after a release, else 15; ratio 1.5%, min 15 | Small distances cut drags |
| `Rapid_Trigger _async_ratiocs` (2026-01-04) | Decay + Diagnostics | STABLE_RATIO variant with a buggy `Consume` | Superseded |
| `Rapid_Trigger.cs` (2026-01-12) | Rapid Trigger_0112 | **Last used.** Pure peak; release when peak − p > min(10 + 0.2·p, 400); press when p > trough + 3; air cut < 10 | Baseline for comparisons (settings above) |

Recurring lessons: per-report constants change meaning when the report rate changes (300 → 1000 Hz).
Smoothing/velocity filters add lag and fire on wobble without considering how far the pen fell. Online
self-tuning drifts. File I/O on the report thread adds latency. Raw pressure output plus a non-zero tip
threshold delays presses.

The 0112 core, for reference:

```csharp
if (!_is_pressed) {
    if (p < _lowest) _lowest = p;
    if (p > _lowest + ActuationDist /*3*/) { _is_pressed = true; _peak = p; }
} else {
    if (p >= _peak) _peak = p;
    double release = Math.Min(BaseStability /*10*/ + p * PressureStabilityScale /*0.2*/, MaxReleaseDist /*400*/);
    if (p < 10 || _peak - p > release) { _is_pressed = false; _peak = 0; _lowest = p; }
}
report.Pressure = _is_pressed ? Math.Max(p, 1) : 0;
```

### `logs/`: old recordings (Windows, before this rewrite)

| File | Content |
|---|---|
| `pressure_log_detailed.csv` (2026-01-28, 23,769 rows) | `Timestamp,X,Y,Pressure`, DateTime ticks (100 ns, ~1 ms resolution), **~332 Hz** (3–4 ms intervals), 78.8 s, max pressure 8191 (saturates often). Mostly rapid tapping *without lifting* (oscillating ~1500–2500 ↔ ~5300) plus holds; 40 contact strokes, the longest 3635 reports. Noise σ ≈ 3 (rt's 2nd-difference estimate; it suggests Activation Distance ≥ 19). No labels for intent. `rt` reads this format directly. |
| `pressure_log.txt`, `pressure_log1.txt` (2025-11/12) | One pressure value per line after a `--- Pressure Log Start ---` header; no timestamps (`rt --rate` assumes a rate). |
| `RapidTrigger_Diagnostic.txt` (12-16) | Event log from STABLE_RATIO: `[time] [ACTIVATE/RELEASE] reason \| P \| Peak \| R \| V`. Releases at ratio 6–15%, plus PANIC releases. |
| `RapidTrigger_Log_ingame.txt`, `_new.txt` (12-08/10) | flux_stable event logs: "Fresh Touch", "Velocity Cut (-155 < -80)". |
| `RapidTrigger_Log_ratio.txt` (12-13), `_valley.txt` (12-14), `_Log.txt` (12-15) | ratio / valley variant event logs (Floor, Ratio, Panic, Release (R, V)). |
| `RapidTrigger_Log_spiral+fastflicks.txt` (12-14) | A "Hybrid (PredRatio)" variant not kept in the folder; releases at predicted ratio 25–37%. |

If the cloud session needs this data, ask the owner to commit `pressure_log_detailed.csv` into
`recordings/` (it is the only timestamped real log).

### `TheSaturnCollection-main/`: example OTD 0.6.6.2 plugins (reference only)

A collection of third-party filters (namespace `Saturn`) used as a template for modern plugin
conventions:
- csproj: `net8.0`, `Nullable` enabled, `PackageReference OpenTabletDriver.Plugin` and
  `OpenTabletDriver` 0.6.6.2.
- `HRRNC` ("High Report Rate Noise Compensation", PreTransform, synchronous position filter).
- `Multifilter : AsyncPositionedPipelineElement<IDeviceReport>` (PreTransform), with Kalman,
  interpolation and prediction; `[TabletReference] public TabletReference TabletReference { set { ... } }`.
- `OutputModeAware` base: resolves the active output mode via `[Resolved] IDriver` to get area/scaling.
- Relevance: if the owner runs Saturn Multifilter or another async/interpolating filter, put Rapid
  Trigger **before** it. Downstream async filters re-emit at their own rate; time in the engine comes
  from `Stopwatch`, so the logic is unaffected.
