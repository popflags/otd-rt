# Rapid Trigger for OpenTabletDriver: notes for Claude

Read this before changing the trigger logic. It records what was tried, measured and decided, including
material that only exists on the owner's Linux machine and is git-ignored (`otd-rt-old/`,
`TheSaturnCollection-main/`, `logs/`). The cloud session does not have those folders; everything
useful from them is summarized below.

## Owner and goal

- Owner (GitHub `popflags`) plays rhythm/aim games with a **Wacom PTK-670 (Intuos Pro M, 2025)** running
  **custom firmware**: since 2026-10-14 **pen pressure sampled at 500 Hz**: each new value appears on one report and
  is repeated exactly on the next (`recordings/mixed-*-500hz-20261014.csv`). Production since 2026-10-18 is ring500
  **0fc2521a**, used at **1000 Hz with motion sync ON** (locked to USB frames): 1000 reports/s, a sample every
  **2.000 ms** (999 Hz sync OFF and the 2026-10-14..17 builds: ~998 reports/s, 2.004 ms).
  Before: pressure every 5 ms (2026-10-05, `recordings/play-ptk670-5ms-20261005.csv`) and every ~9 ms
  (`recordings/play-ptk670-20261001.csv`), 1000 reports/s. On OpenTabletDriver (OTD) **0.6.7** (latest release;
  plugin targets it). Develops on Linux (CachyOS) locally and on Windows through Claude Code in the cloud (no IDE
  on Windows).
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
src/RapidTrigger/DiagnosticsRecorder.cs  per-report CSV via Channel + background thread (t_ms host time, engine_ms engine time)
src/RapidTrigger/FirmwareSampleClock.cs  opt-in report timing from the PTK-670 firmware's byte-17 sample counter, OTD-free
src/AnglePreservingSensitivity/          separate plugin DLL, own version: relative-mode direction-preserving X:Y speed
tools/RtTool/                            `rt` CLI: replay + calibrate; compiles TriggerEngine.cs via <Compile Link>
tools/Sweep/                             `sweep` CLI: scores settings grids on recordings + synthetic signals (how defaults are chosen)
tools/Tune/                              `tune` CLI: scores settings on the 2 ms gameplay recordings (labelled re-clicks / drag dips) + 2 ms drags
tools/td2csv.py                          OTD Tablet Debugger log (tablet-data_*.txt) -> CSV with engine_ms from the byte-17 counter
tools/synth_logs.py                      regenerates syn_drag.csv / syn_tap.csv (synthetic 1 kHz reports; --sample-ms N repeats, --samples-only)
tests/RapidTrigger.Tests/                xunit tests on synthetic 1 kHz report streams (Signals.cs), FirmwareSampleClock tests
recordings/                              real diagnostics CSVs from the tablet (commit them here)
scripts/cloud-setup.sh                   SessionStart hook: installs the .NET 8 SDK in cloud sessions
.github/workflows/ci.yml                 build + test + replay recordings on every push, uploads DLL artifact
.github/workflows/release.yml            on a new <Version> (main, claude/*) or tag v*: GitHub release with RapidTrigger.zip/.dll + RtTool.zip
```

## Commands

```sh
./build.sh                                         # test + build + dist/RapidTrigger.zip + dist/RtTool.zip
dotnet test tests/RapidTrigger.Tests
dotnet tools/RtTool/bin/Release/net8.0/rt.dll replay <csv>... [--set Name=Value]... [--events]
dotnet tools/RtTool/bin/Release/net8.0/rt.dll calibrate --drag <csv>... --tap <csv>... [--margin 0.35]
python3 tools/synth_logs.py /tmp/syn                 # synthetic logs for quick experiments
dotnet run -c Release --project tools/Sweep -- "FastFallSpeed=1|1.5|2,FastFallPercent=0.2|0.22"   # score a grid
dotnet run -c Release --project tools/Tune -- --margin "FastFallSpeed=10|12|14,ActivationPercent=0.5|0.75"  # owner's 2 ms data
python3 tools/td2csv.py tablet-data_1792424513.txt out.csv     # OTD debugger recording -> replayable CSV
```

In cloud sessions `scripts/cloud-setup.sh` installs the SDK (apt `dotnet-sdk-8.0`, falling back to
dotnet-install.sh into `~/.dotnet`). If `dotnet` is missing, run `scripts/cloud-setup.sh --force` and
use `~/.dotnet/dotnet`. Shell note for the owner's local machine: the login shell is fish and the tool
shell is zsh. `$VAR` with spaces doesn't word-split there, so wrap loops in `bash -c '...'`.

### Testing on Windows without building

Every push runs CI and uploads `RapidTrigger-<sha>` (RapidTrigger.zip + RtTool.zip) as an artifact on
the Actions run page. The owner extracts RapidTrigger.zip into `%localappdata%\OpenTabletDriver\Plugins\`
and restarts OTD. Releases: bump `<Version>` in `src/RapidTrigger/RapidTrigger.csproj` and push to `main` or a
`claude/*` branch; `release.yml` creates the `vX.Y.Z` tag and the GitHub release when that tag does not exist
yet (cloud sessions cannot push tags). Pushing a `v*` tag by hand still works. Commit messages end with the Co-Authored-By trailer.

## OpenTabletDriver facts (verified in its source, v0.6.7 and branches as of 2026-10)

- Plugin API: `IPositionedPipelineElement<IDeviceReport>` with `Consume`, `event Emit`, `Position`.
  Attributes `PluginName`, `Property`, `BooleanProperty(name, desc)`, `DefaultPropertyValue`, `ToolTip`,
  `Unit`; `[TabletReference] public TabletReference X { set; }` is injected at construction.
  NuGet `OpenTabletDriver.Plugin` **0.6.7**, referenced with `ExcludeAssets="runtime"`.
- Filters are constructed on every settings apply (properties set before the first `Consume`).
  The output mode disposes `IDisposable` elements.
- Pipeline: PreTransform elements → transform → PostTransform elements → output, in profile list order.
  `BindingHandler` is **appended last** as a PostTransform element.
- **Tip binding in 0.6.7** (`ThresholdBindingState`, inside the BindingHandler, so *after* all filters):
  `pressed = pressure% > TipActivationThreshold` (strictly greater; 0.6.7 added the special case that a
  100% threshold fires at exactly max pressure), then pressure is remapped above the threshold.
  Consequence: if a filter passes raw pressure and the tip threshold is above 0%, presses are delayed
  until raw pressure exceeds it. That is why the plugin reports **full MaxPressure while pressed** by
  default (Preserve Pressure off).
- **Coming after 0.6.7 (0.6.x branch, commit ea34ceee, 2026-07, unreleased):** the threshold moves to
  `PressureRewriteFilter`, which is **prepended before all user filters**
  (`elements.Prepend(pressureRewriteFilter).Append(bindingHandler)`). It remaps
  `p → max·(p% − t)/(1 − t)` and sets anything ≤ t to 0, except reports already at max. The binding then
  fires on `pressure > 0`. With a non-zero tip threshold, Rapid Trigger would see remapped pressure
  (later contact, scaled distances, noise amplified by 1/(1 − t)). **Users must set the tip threshold to
  0%**, which is identity in both versions. If that release ships, consider detecting/warning about it.
- 0.6.7 vs 0.6.6.2 otherwise: OutputMode builds the chain as Pre → transform → Post in one list (same
  order), Dispose pattern changes, wheel binding rework, PTK-670 config gained `Wheels` (and on 0.6.x
  `MinRotation/MaxRotation`), and the IntuosV3 pen report parsing is unchanged.
- **master = 0.7.0.0**, last commit 2025-12-05, nearly dormant, unreleased. The API is reorganized: no
  separate Plugin assembly, namespaces `OpenTabletDriver.*`, filters implement `IDevicePipelineElement`.
  Not useful now; porting would mean rewriting only `RapidTriggerFilter.cs` (the engine is OTD-free).
- Out-of-range: `OutOfRangeReport` (struct, namespace `OpenTabletDriver.Plugin.Tablet`). The binding
  handler only releases *pen buttons* on it, not the tip.
- PTK-670 config (0.6.7): `MaxPressure` 8191, digitizer 52600×29600, parser
  `OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV3.IntuosV3ReportParser`.
  - **Custom firmware since 2026-10-14:** reports are ID **0x1E** → `IntuosV3ExtendedReport` (struct, same
    interfaces): X = bytes 3–5 (24-bit LE), Y = 6–8, pressure = bytes 9–10 (u16 LE), tilt 11–12 / 13–14, status
    byte 2 (bit 6 = near proximity, bit 5 = eraser), `HoverDistance` byte 19. Out of proximity also arrives as
    0x1E with NearProximity false and pressure 0 (not a `DeviceReport`/`OutOfRangeReport`). Byte 17 (not parsed
    by OTD, read from `Raw`) is a pen-read counter: u8, wraps, +1 on every report carrying a new pressure sample,
    unchanged on the repeat report (each step = 2.004 ms). ~98.5% of pressure changes land on the stepping reports.
  - Stock firmware (and the custom firmware before 2026-10-14): `0x1F` reports with `data[1]==0x01` →
    `IntuosV3Report` (pressure = ushort at byte 7, `HoverDistance` byte 13); other `0x1F` reports, i.e. out of
    proximity, become plain `DeviceReport`.
  - Both report types are **structs**: mutating `Pressure` through the `ITabletReport` interface changes the boxed
    report in place, which is how the filter works.
- There is no timestamp in reports; the filter times reports with `Stopwatch` at `Consume`, capping gaps at 50 ms.
  The host receives them in **bursts**: on the 2026-10-14 recordings, report intervals are ≤ 0.13 ms for 25% and
  ≥ 2.8 ms for 10%, and two consecutive pressure samples (always exactly 2 reports apart, none lost while the pen is
  down) arrive anywhere from 0.2 to 4 ms apart by the host clock (median 1.95). Opt-in **Firmware Sample Clock**
  (`FirmwareSampleClock`) times 0x1E reports by the byte-17 counter instead: each report gets the counter steps it
  carries × `SampleMs` (0 if the counter did not step), and the engine adds repeats to the next sample, so every
  sample gets an exact multiple of `SampleMs` (counter difference since the previous sample's report). Host time for
  other reports, after 50 ms gaps, and for good if the counter's time differs from the host clock by more than
  ×1.25 over 2 s (warning in the OTD log).
  - **`SampleMs` = 2.000 ms since 2.8.2** (ring500 0fc2521a at 1000 Hz motion sync ON; owner: handle only that mode).
    2.004 ms before (cdb17e54 and earlier). Other modes on 0fc2521a, deliberately not handled: 999 Hz sync OFF
    samples every 2.004 ms (clock 0.2% slow, negligible); analog pens (CP-923 etc., new in 0fc2521a, deferred by the
    owner) get a new pressure on every report and step byte 17 once per report (1.004 ms), so the counter's time runs
    2x fast and the host-clock check falls back to host time (permanent until settings are reapplied). The firmware
    handoff suggests measuring the period from the stream (host time / steps over 1-2 s, clamped 0.95-2.1 ms) if those
    modes are ever needed. Some samples are up to +1 ms longer while the counter still steps by 1 (an ExpressKey /
    wheel report gets its own USB slot while the pen is in range; +0.2 ms during backlog drains): counter × period
    underestimates them, accepted. Also on 0fc2521a: reports arrive ~1.3 ms fresher (pen sample to USB done median
    0.72 ms), the 1 Hz battery report 0x13 is gone, 0x1E layout / saturation fix unchanged. Calibration from older
    recordings should be redone on 0fc2521a at 1000 sync ON.
  - In ~1.5% of samples the new pressure arrives one report *after* the counter step: per report (S = counter
    stepped, p = pressure changed) `[S p][. _][S _][. p][. _][S p]`. 2.8.0's clock split every period evenly (1.002
    ms per report, the stepping report getting 2.004 minus the half already given); there the late sample got
    3.006 ms and the next one 1.002 ms, half its drag allowance. Measured on real logs with the counter (pressure +
    byte 17 per report): samples got 1.002 ms 1.45%, 2.004 93.4%, 3.006 1.49%, 4.008 2.3%, 6.012 0.33%. Since 2.8.1
    each report gets only its own counter steps: 2.004 ms 97.3%, 4.008 2.4% (equal-value samples merged), 6.012
    0.3% (`SampleArrivingOneReportAfterTheCounterStepStillGetsExactly2004Ms`).

## The algorithm (TriggerEngine)

Per report: `Update(rawPressure, elapsedMs)`, but the state machine runs on **pressure samples**: a report whose
pressure equals the last sample exactly only adds its elapsed time to a pending total and returns None (no state
changes); the next differing value is processed with pending + its own time. A repeated stream therefore gives the
same events at the same sample times as the samples alone (`RepeatedSamplesGiveTheSameEventsAsTheSamplesAlone`), and
a tablet with a new value every report is unaffected. Exceptions, because they are timers: while a phantom contact
waits for confirmation, repeats are processed (the 8191 level can sit still for 20 ms); during a dropout the zeros
are never repeats (the last sample stays at the pre-drop level). Constant real pressure (e.g. saturated at 8191) is
indistinguishable from repeats: its time is carried to the next change. A release held back by `PressHoldoff`
fires on the report where the holdoff ends, repeat or not.

- **Released:** the trough tracks the minimum (optional `PressDriftTimeConstant` lets it creep up).
  - *Fresh contact* (pressure hit ≤ `LiftThreshold` since the last release): press on the first report
    ≥ `ContactThreshold`. Exception (2.3.1): a contact whose first report is ≥ `PhantomContactPressure`
    (8191) waits `PhantomConfirmTime` (20 ms), presses as soon as pressure moves below that level, and is
    dropped if the pen lifts first.
  - Otherwise *Rearm*: press when pressure ≥ trough + `ActivationDistance`.
- **Pressed:** whichever detector fires first releases.
  1. **Lift:** pressure ≤ `LiftThreshold`. Exception (dropout guard, 2.5.0, widened in 2.5.2): a fall straight
     from ≥ `DropoutPressure` (6000) to ≤ lift is held back `DropoutTime` (60 ms), in **both** states. If
     pressure comes back above lift in that time the zero is ignored: previous pressure stays at the pre-drop
     level (8191 → 0 → 3000 is judged as 8191 → 3000), no release while pressed, no fresh contact / lowered
     trough while released. Otherwise it counts as a lift. See the dropouts recording below.
  2. **FastRelease (CUSUM):** on each pressure sample,
     `FallExcess = max(0, FallExcess + (prev − p) − (FastFallSpeed + FastFallPercent%·prev)·dt)`, with dt = time since the previous
     sample, repeated reports included (before 2026-10-01 dt was the report interval, which on the PTK-670 made the
     allowance 9× too small; v2.0.1 has that bug). Release when ≥ `FastReleaseDistance` (× hold multiplier).
     Only fall faster than the drag allowance accumulates. No smoothing: it reacts on the first fast sample.
  3. **Release (slow path):** hold reference − p ≥ `ReleaseDistance` (or `ReleaseRatio`·ref, capped by
     `MaxReleaseDistance`, × hold multiplier). The hold reference jumps to new peaks and drifts toward the
     **previous** sample with `DriftTimeConstant`, so a sudden drop is measured in full. It does not drift
     while `FallExcess > 0`.
- **Released, Rearm:** rise ≥ `ActivationDistance + ActivationPercent%·trough` (2.3.0).
- Fast-detector allowance × `TapAllowance` (0.4, after a Contact) or × `RepressAllowance` (0.5, after a Rearm) at
  press start, ramping linearly to × 1 over `HoldTime` (250 ms) (2.6.0, split in 2.7.0).
- `PressHoldoff` (8 ms, 2.7.0): for this long after any press only Lift releases (detectors keep tracking). Since 2.9.0 a
  release that came due inside it is dropped at its end (detectors restart from that sample) if the last sample did not fall and
  pressure is back up from its low since the press's peak by the re-press distance: releasing there only re-pressed on the next
  sample (landing overshoot 3706 -> 3337 -> 3616). A tap whose own fall has started by then still releases at the holdoff end.
- Defaults (2.7.0, unchanged in 2.8.0): Contact 4, Lift 2, Phantom 8191 / 20 ms, Dropout 60 ms from ≥ 6000, Activation 10 + 0.5 %,
  FastFallSpeed 2 raw/ms, FastFallPercent 0.19 %/ms, FastReleaseDistance 10, TapAllowance 0.4, RepressAllowance 0.5,
  PressHoldoff 8 ms, HoldTime 250 ms,
  ReleaseDistance 600, ReleaseRatio 0, Max 1000, Drift 10 ms, PressDrift 0, HoldMult 1.

### Evidence behind the design and defaults

**2026-10-19 gameplay logs (Windows, ring500 83a52e3c, 1000 Hz sync ON), `recordings/play-ptk670-2ms-{a,b}-20261019.csv`; 2.9.0
and the owner's settings.** Owner: "find my best settings without any light drag cutouts (I mean 0)"; "some reactivations just
don't count for me in these logs or do not reactivate fast enough"; "initial clicks obviously need to be as fast as possible"; the
Windows config is the main one (Linux keeps Relative Mode + MouseBinding, Windows VMulti; both get the same Rapid Trigger numbers).
Scored with `tools/Tune` (columns in its header).
- Initial clicks: all 185 contacts press on their first sample (first sample ≥ 4 every time); all 11 contacts starting at 8191 are
  one-sample phantoms. Nothing left to gain on the press side.
- Landing: the first sample often overshoots and the next one dips 250–530 (e.g. 0 → 4311 → 3038 → 3302 …). With the owner's
  Tap Allowance 0.1 the dip made a release due inside the 12 ms holdoff, which fired at its end and re-pressed a sample later (5
  double clicks). The 2.9.0 holdoff rule removes 4 (the 5th, 671 → 1725 → 472 → hold, is a real 1250 fall). Cost: two very short
  taps (38.16 s, 171.78 s in b) release 6 / 4 ms later, because the old release fired on a rising sample before their fall began.
  With the defaults it removes in-stroke re-presses on the 2026-10-14 mixed logs (427 → 282, 177 → 170, 328 → 324) and newtip
  (7 → 5); no contact changes on any recording, the 5 / 9 ms play logs, rt-* and syn_drag are unchanged.
- In-stroke dips split cleanly by the fastest fall over 10 ms of samples: slow drag dips ≤ 11 raw/ms (one at 13.8; 900–1000 over
  250–320 ms, pen stays down) vs re-clicks 29.6–65 raw/ms. The previous Windows settings (FastFall 40 raw/ms + 0 %, FRD 30, Activation
  10 + 1 %) needed one 2 ms sample falling > 80 to release: the 164.7 s re-click (30 raw/ms) never released, the others late. The
  Linux settings (4 + 0.1 %, FRD 40, Activation 5 + 0.1 %) released 2 slow drag dips and made 17 extra double clicks.
- Light drags: tools/Sweep's set (dip phase 0 at contact) is too easy at 2 ms; tools/Tune draws the dip phase at random (20 seeds per
  level, 150–1000, ±10/±15, ±20 reported), landing ramp ending on the drag's own start value (a ramp to the full level then a step to
  the dip phase is an artefact that cut every config).
- Result, previous Windows (2.8.2 engine) → **owner's settings since 2026-10-19 (2.9.0): FastFall 12 + 0.2 %, FRD 35, Tap 0.1,
  Repress 0.3, HoldTime 400, PressHoldoff 12, Activation 10 + 0.75 %, ReleaseDistance 300** (rest unchanged: Contact 4, Lift 2,
  Phantom 8191 / 20, Dropout 3 / 8190, Ratio 0, Max 1000, Drift 10, PressDrift 0, HoldMult 1, Firmware Sample Clock on):
  light cut-outs 0 → 0 (also at ±20 and up to 2000), medium 0 → 0, re-clicks caught 5 → 6 of 6, released 18.8 → 34.3 ms before the
  trough, re-pressed 19.6 (caught ones) → 12.3 ms after it, slow drag dips released 0 → 0 with margin 2.46 → 1.94, tap lead before
  lift 32.4 → 33.4 ms, hold lead 40 → 54 ms, landing double clicks 5 → 1, other double clicks 3 → 5 (two-stage lifts: a fast fall,
  a plateau with a +64…+141 wobble, then the lift; and a slow +64 creep on a plateau), synthetic re-presses 952 → 1029.
- Trade-offs measured: re-press distance 10 + 0.5 % re-presses 2 ms sooner but adds a double click on a three-stage lift (85.9 s,
  +45 at 5928); 10 + 0 % 5 ms after the trough but 12 double clicks. PressDriftTimeConstant 20–200 barely changes them. Margins
  1.1–1.5 (e.g. 6 + 0.1 %) release re-clicks up to ~10 ms earlier but are one slightly faster drag dip from a cut-out; ReleaseDistance
  200 adds ~4 ms on re-clicks, margin 1.76 and a medium-drag cut-out.
- Defaults were not changed (still tuned for 5 ms; far too sensitive at 2 ms: hundreds of light cut-outs).

**500 Hz pressure firmware (2026-10-14), `recordings/mixed-*-500hz-20261014.csv`:** three unlabelled mixed-use
recordings (strokes+taps 116 s / 331 strokes, lines+taps 118 s / 267, long drags 174 s / 47 with 7 over 1 s). Host
timing only (no byte-17 column; `pressed`/`event` columns are 0). 26 one- or two-sample phantom 8191 contacts
(filtered by 2.3.1) plus one phantom 8191 sample right before a real contact.
- **2.8.0** merges this work with 2.5.2–2.7.0, which had been released from a branch that never reached `main`.
  Merged vs 2.7.0 on all seven recordings and the synthetic sets: identical presses, re-presses, cut-outs and lead
  before lift; only the release-timing metric moved (per-sample descent start).
- The change request for this firmware quoted v2.0.1 numbers (per-report CUSUM; 500 Hz-repeated `syn_drag` 3
  cut-outs vs 0 samples-only; recordings sigma 10.9/6.7/5.4 with repeats counted). On 2.5.1 the CUSUM and
  `NoiseSigma` already worked per sample: repeated and samples-only synthetic sets gave identical counts, sigma
  3.6/2.4/2.4. The engine change (repeats change no state at all) and RtTool's per-sample descent start therefore
  changed no press/release count on any file; only release timing moved by one report (p50 0.9 → 2.8 ms on
  strokes-taps), because it no longer counts from the repeat report.
- **Defaults (tuned for 5 ms samples) are far too sensitive here.** Replay with the 2.7.0 defaults (unchanged in
  2.8.0; contacts / re-presses inside strokes): strokes-taps 331 / 427, lines-taps 250 / 177, long drags 39 / 328
  (2.5.1: 520 / 207 / 330 re-presses). The allowance is per ms, so a 2 ms sample gets 2/5 of a 5 ms sample's slack
  against the same per-sample noise.
- **Host-timing jitter adds to it.** Replaying with an ideal report clock (report index × 1.002 ms, real gaps > 5 ms
  kept; close to the Firmware Sample Clock, except that the ~1.5% late samples got 3.006 ms and their followers
  1.002 ms there, like the 2.8.0 clock) at the same defaults: re-presses 427 → 391, 177 → 116, 328 → 309
  (2.5.1 defaults: 520 → 488, 207 → 143, 330 → 275).
  Example on strokes-taps at 13.14 s: contact at 2822, FastRelease 4 ms later on a 22-unit dip that arrived a
  fraction of a ms after the previous sample by the host clock. Not adopted as default (owner's request: generic
  path stays default) and not measurable on real data until recordings are made with it on (they then carry
  `engine_ms`).
- **Open:** recalibrate the defaults for 2 ms samples from labelled `drag-*`/`tap-*` recordings made with this
  version (Enable Diagnostics on, ideally Firmware Sample Clock on), then `rt calibrate` / `tools/Sweep`.

**2.7.0, owner: "squeeze every last drop of stable-ish speed from 200 Hz, ms matters"; also cares about consistency
for rhythmic clicking.** Tools: tap release time vs a fixed 3 %-of-peak trigger on the same taps (both 5 ms logs, 120
taps; differences between configs are real time differences), extra clicks (re-presses) inside taps / holds
(strokes merged across dropout zeros < 60 ms), and the Sweep synthetic sets.
- Consistency: release spread vs a fixed-fraction trigger is ~2.1–2.5 ms SD, of which 1.4 is 5 ms sampling. No
  FastFall/FRD setting lowers it by more than ~0.1 ms without slowing taps or cutting a hold; the rest follows the
  owner's lift speed (a slow lift needs one or two more samples to beat hold wobble). Force-independent (allowance
  scales with pressure); taps ≥ 150 ms release ~2 ms later relative to shorter ones.
- The ramp length mattered more than its depth: TapAllowance 0.5 over 250 ms beat 0.3 over 150. Real 5 ms logs: no
  hold cut down to 0.05 / 400 ms. The 9 ms logs found two failure modes: (a) after an accepted mid-hold re-press,
  a low ramp restarted on the Rearm cut the rest of the hold again (newtip 44.1 s, 55.4 s) -> separate
  RepressAllowance, 0.5–1 all clean; (b) a two-stage hold start (newtip 190.1 s: 5104 -> 4977 over ~27 ms at
  +60 ms) cuts with TapAllowance ≤ 0.3, holds at 0.4.
- Early-drag tolerance (first dip that cuts within 400 ms of landing, 5 seeds, ±7.5 noise, 1 % / 2 % tremor, at
  5000): 0.15/250 9 % / 2 %, 0.3/250 12 % / 4 %, **0.4/250 13 % / 6 %**, 0.5/150 (2.6.0) 19 % / 12 %, no ramp 22 % / 16 %.
- Chosen 0.4 / 0.5 / 250 ms + PressHoldoff 8: taps release ~1.0 ms earlier on average than 2.6.0 (+1.03 -> −0.01 ms
  vs the 3 % reference), SD 2.05 -> 2.34; per event 25 releases earlier on the 5 ms logs, the only "later" ones are
  two recording-start artefacts and the 45.20 s landing bounce, now one tap instead of a double click; double
  clicks inside taps 4 -> 2 (5 ms logs) / 4 -> 3 (9 ms); no new hold cut on any log. Synthetic: re-presses 1146 ->
  1163, light ±10 0 -> 1, ±15 95 -> 122, medium 14. Faster option documented: TapAllowance 0.15 (~1.5 ms more,
  case (b) cuts, early-drag tolerance 9 % / 2 %).
- PressHoldoff 8 ms removes the landing bounces at 20.47 s (5065 -> 4970 -> 5207) and 45.20 s; shortest real tap
  ~28 ms. 12 ms holdoff was no better on the logs and costs more spread.

**2.6.0, owner: "make sure it's as fast as it can ever be, speed is the most important."**
- What 200 Hz bought (same strokes of both 5 ms logs, every other sample dropped to simulate 10 ms): presses
  3.0 ms earlier on average (p90 5), final releases 4.5–4.9 ms earlier (p90 10).
- Presses: 155 / 156 contacts on the 5 ms logs press on their first report; the exception is 0 → 8191 → 1966
  (phantom sample just before a real touch, 5 ms). Nothing left there without clicking on post-lift phantoms.
- Releases vs an oracle that releases on the first falling sample after the final peak (unreachable: hold
  wobble has falling samples too): taps 15.3 ms behind on average with 2.5.2. Frontier over FastFallSpeed
  0–2 × FastFallPercent 0.1–0.19 × FRD 3–10 on both 5 ms logs: every faster point adds real cut-outs (first one:
  a hold dip 6774 → 6254 over ~55 ms at 83.56 s of the dropouts log); 0 / 0.1 / 3 is 5 ms faster but adds 13.
  So 2 / 0.19 / 10 stays.
- `TapAllowance` (reconsidered; see the 9 ms notes: same idea as "allowance ramping from low (taps) to high
  (holds)", a time-based tap/hold split in the TimerwThreshold/HSK family, but one detector with a continuous
  ramp). On the 5 ms data: 0.5 over 150 ms → taps 15.3 → 14.4 ms behind the oracle, 22 releases one sample
  earlier and none later, no new real cut-out, hold margin unchanged (1.35); 9 ms logs 47 earlier, +1 landing
  bounce (old log 1.18 s: 3996 → 3965 → 4016). Synthetic: re-presses caught 1121 → 1146, light drags ±10 still 0,
  ±15 42 → 95, medium 13 → 14; a drag dipping from the moment it lands tolerates ~11 % dips instead of ~14 % for
  the first 150 ms (`DragDippingRightAfterContactIsTheTapAllowanceTrade`). 0.5 / 250 ms gained 0.5 ms more but
  started cutting light drags; 0.3 adds medium cut-outs.
- Activation 20 → 10 (+ 0.5 %): synthetic re-press delay 8.4 → 7.4 ms (with TapAllowance), no change on any real
  log. Margin on the +39 wobble at 6976 (old log): 44.9 / 39 = 1.15 (was 1.41). 10 + 0.25 % adds a re-press on
  the 5 ms log.

**Pressure dropouts (2026-10-05), `recordings/play-ptk670-5ms-dropouts-20261005.csv`:** 90 s of gameplay,
5 ms sampling, 2.5.1-era build (the live guard only covered drops from exactly 8191 for 12 ms). The firmware
reports **0 when pressed hard**: one-sample zeros from 7750–8121 that return to the same level (8038 → 0 →
8012, 7896 → 0 → 7890, 7752 → 0 → 7746), and while saturated whole bursts alternating 8191 / 0 for 300–500 ms
with zero runs of 5–45 ms (57 of 74 zeros last one sample). Bursts can start straight from below max
(7846 → 0, 8121 → 0). It looks like an overflow: values above max come out as 0. The 2026-10-05 5 ms log has one
too (8169 → 0 for 7 ms → 8164 at 9.95 s, previously counted as two strokes). The owner predicted this.
- Every real lift in all four logs (zero lasting ≥ 60 ms after a pressed stroke) came from ≤ 3038 (5 ms logs:
  ≤ 2692): pressure always passes through lower values first. The only drops from ≥ 5000 straight to a long zero
  are one-sample phantom 8191 strokes. Hence DropoutPressure 6000 (dropouts ≥ 7752, lifts ≤ ~3000) and
  DropoutTime 60 ms (longest dropout 45 ms); on the recordings the guard never delays a real lift.
- Replay, 2.5.1 guard → 2.5.2 guard: dropouts log presses 65 → 55 (10 false lift + re-press pairs gone, incl.
  two holds cut for ~350–400 ms at 73.5 s and 76.9 s), lead before lift p10 0 → 25 ms; 5 ms log 106 → 105
  presses (the 9.95 s dropout); 9 ms logs unchanged.
- Same log: some strokes have reports every ~3 ms with a new pressure each (20.0–20.6 s, 23.8 s, 28.0 s). Two
  landing bounces right after contact release and re-press within ~5 ms (20.47 s: 5065 → 4970 → 5207; 45.20 s:
  4021 → 3839 → 4209), plus a wobble at a tap's top (45.71 s). Not changed (same kind as the accepted landing
  re-presses); a short post-contact release hold-off would fix them at the cost of very short taps.

**5 ms firmware (2026-10-05), `recordings/play-ptk670-5ms-20261005.csv`:** 84 s, recorded with 2.3.x/2.4.0
defaults. Pressure changes every 5 ms (1000 reports/s). 106 strokes, all lifted: ~93 taps, 13 holds ≥ 300 ms
(up to 8191). Two phantom 8191 samples, both right *before* a contact (0 → 8191 → 1966 at 25.14 s, 8191 → 0 for
6 ms → 1495 at 81.11 s); 2.3.1's filter handles both. One landing-wobble re-press (stroke at 62.4 s,
3217 → 3134 → 3084 → 3128), the same kind as the accepted ones on the newtip log. Steady-hold noise σ ≈ 3–5
per sample at 5000–8191 (second differences, repeated values skipped); light-pressure noise still unknown.
- What 5 ms sampling changes: the allowance is per ms, so drag/tap *speeds* transfer unchanged, but sensor noise
  is per sample and the per-sample allowance shrank to 5/9. The 2.3.0 defaults (0.5 + 0.25 %) give 2.5 + 0.25 %·p
  per sample; synthetic light drags (500–1000, 10 % dips, 1.5 % tremor, `tools/Sweep`) went from 9 cut-outs at
  9 ms to 89 at 5 ms (±10 and ±15 noise combined).
- Scored with `tools/Sweep` (columns explained in its header). 2.3.0 vs **2.5.0 (1.5 / 0.22 / 10)**:

| | 2.3.0 (0.5 / 0.25 / 10) | **2.5.0 (1.5 / 0.22 / 10)** | 3 / 0.2 / 10 | 3 / 0.25 / 10 |
|---|---|---|---|---|
| 5 ms log: tap / hold lead before lift, mean ms | 43.5 / 87.9 | 43.5 / 87.9 | 43.0 / 87.9 | 41.8 / 85.0 |
| 5 ms log: hold margin; old 9 ms log margin | 1.52; 1.42 | 1.46; 1.34 | 1.51; 1.35 | 1.79; 1.61 |
| synthetic no-lift re-presses caught (of 1216, 5 ms), delay | 1101, 8.4 ms | 1101, 8.3 ms | 1079, 8.2 ms | 1015, 8.0 ms |
| light drags 3×8 s, ±10 noise: cut-outs | 7 | 0 | 0 | 0 |
| light drags, ±15 noise | 161 | 61 | 3 | 1 |
| medium drags 2×8 s (2500/5000, 15 % dips, 2 % tremor), ±5 / ±10 / ±15 | 1 / 8 / 18 | 3 / 8 / 16 | 1 / 5 / 10 | 0 / 0 / 2 |

  Per event vs 2.3.0: the 5 ms log releases 2 strokes one sample earlier and 1 later (55.49 s, light pressure
  ~3000); the 9 ms logs release 8 and 15 strokes one sample earlier, none later; presses unchanged everywhere.
  So 2.5.0 is speed-neutral on real data and fixes light drags at realistic noise. The higher fixed part buys
  light-pressure noise tolerance, the lower percent keeps heavy-hold releases as fast as before.
  3 / 0.2 / 10 was the first pick (robust to ±15 light noise) but released 12 of 106 real 5 ms strokes one
  sample (5 ms) later, all at 2300–3800 pressure. 3 / 0.25 is the "steady drags" preset if real drags cut out.
- **2.5.1, owner feedback:** "some taps without a full lift or purposeful reactivations are ghosting through, make it
  a bit more sensitive." `sweep --misses`: every missed synthetic re-press is a missed *release* (the re-press side
  never misses); all are shallow/slow re-taps at 1500–4000 (depth 200–400, period 70–150 ms) whose fall stays
  under the allowance. Fix: FastFallSpeed 1.5 → 2, FastFallPercent 0.22 → 0.19 (allowance lower above ~1700,
  higher below, so light drags stay as robust as 2.5.0). Compared with 2.5.0:
  - Synthetic re-presses caught 1101 → 1121 of 1216 (noisy set). Noise-free, at 3000–5000 re-taps release from
    ~50 units shallower (e.g. 450 deep / 120 ms at 4000: 0 → all), nothing changes at 1500
    (`ShallowRePressesAt4000SampledEvery5MsAreDetected`).
  - Real logs: 4 / 8 / 24 releases one sample earlier (9 ms, 5 ms, newtip), none later; one new release + re-press
    on the newtip log at 211.1 s (8191 → 7120 in ~80 ms, ~18 raw/ms, back to 7356, lift 120 ms later; same kind as
    the intended stroke 68).
  - Costs: hold margin 1.46 → 1.35 (5 ms log; the 2.3.0 criterion), 1.34 → 1.22 (old 9 ms log); medium synthetic
    drags ±10: 8 → 13 cut-outs; light ±15: 61 → 42 (better); still holds at ±25 noise on 3000 only at 9 ms (at
    5 ms the test now uses ±15, ~2× the measured σ).
  - FastFallPercent 0.20 with FastFallSpeed 1.5 catches slightly more (1127) but light drags at ±15 get worse than
    2.5.0 (78); 2.5 / 0.18 is steadier (light 17) but catches less (1117). Going further (0.17–0.18 with 1.5–2)
    drops the hold margin to 1.1–1.3.
- Medium synthetic drags at 5 ms (11.5 raw/ms worst-case speed vs 11.5 allowance at 5000) sit at the edge for
  every speed-neutral setting. **A real 5 ms drag recording (`drag-*.csv`) is the open item.**
- Phantom Confirm Time 20 ms left as is: phantoms last one sample (5 ms now, 9 ms before), no real contact has
  started at 8191, so a lower value would gain nothing measurable.

**PTK-670 recording (2026-10-01), `recordings/play-ptk670-20261001.csv`:** 108 s of play recorded with the
then-defaults (FastReleaseDistance 40, per-report CUSUM). 103 strokes, every one lifted at the end: ≈70 taps
(10–210 ms) and ≈32 holds (320–810 ms, 6000–8191, likely sliders). No drags longer than 0.8 s, no
re-pressing without lifting, so Rearm/ActivationDistance and long drags are untested on real 1000 Hz data.
Every press is a Contact on the first sample (often already 2000–5000), so press latency = sample timing.
Evaluated as "every stroke is one press": a release followed by a re-press before the lift is a cut-out;
latency = *lead before lift* (ms from release to pressure 0; larger = earlier).

| | 2.0.1 as recorded (per-report CUSUM, 20 raw/ms, FRD 40) | per-sample, FRD 40 | 2.1.0 (per-sample, 20 raw/ms, FRD 20) | **2.2.0 (0.5 raw/ms + 0.25 %/ms, FRD 10)** | old 0112 |
|---|---|---|---|---|---|
| unintended cut-outs (strokes; 68 is intended) | 4 (36, 79, 90, 94) | 0 | 0 | 0 | 8 |
| tap lead before lift, mean | 34.7 ms | 28.2 ms | 29.2 ms | 31.4 ms | 25.6 ms |
| hold lead before lift, mean | 100 ms | 59.5 ms | 60.9 ms | 64.2 ms | 120 ms |
| real hold dips: scale before one would release | < 1 | | 1.67 | 1.41 | |
| synthetic no-lift re-presses caught (of 1064, 9 ms samples) | 1010 | | 517 | 907 | |
| synthetic drags held 9 ms (`synth_logs.py --sample-ms 9`): cut-outs | 69 | 0 | 0 | 26 | |
| synthetic drags, true 1 kHz (`syn_drag`): cut-outs | 0 | 0 | 9 | 238 | 60 |

- **2.2.0, owner feedback:** stroke 68 (fast 1600 dip during a hold, then re-press) was an intended release, and
  the owner accepts compromises for speed. The owner suspected 2.1.0 misses *intentional* re-presses without
  lifting. Confirmed on synthetic no-lift taps (raised cosine, ±15 noise, 9 ms sample-and-hold, low 1500/4000,
  depth 200–2000, period 50–150 ms, 20 taps each): with a fixed 20 raw/ms allowance any re-tap shallower than
  ~600 (or slower than ~100 ms at 600–800) never released, so the re-press was lost. Rearm itself is already
  first-sample (ActivationDistance 40 ≪ a 9 ms rise); the release is the bottleneck.
- Fix: allowance = FastFallSpeed + FastFallPercent% of pressure. Every real hold dip happened at 7000–8191, so
  a pressure-proportional allowance stays high there and drops at lighter pressures. Chosen at 35% margin over
  the real dips (scaling them by 1.41 is the first that releases). 0.20 %/ms catches 965/1064 re-presses but
  only 1.16 margin; 0.22 → 946 / 1.26. With 0.25 %/ms it misses only re-taps ≤ 200 deep at 4000, ≤ 300–400
  slower than ~70–100 ms at 4000, and ≤ 600 at 150 ms; at 1500 everything ≥ 300 deep. Cost: the made-up
  harsh synthetic drags (30% dips at 5000, ~20 raw/ms) cut out, and true 1 kHz sampling needs the old fixed
  allowance (20 raw/ms, 0 %, FRD 40). Low-pressure drags (allowance ~4 raw/ms at 1500) are untested.
- **2.3.0 optimization** (owner: "optimize as much as possible"). Scored per config: unintended cut-outs on the
  play log (68 excluded and required to re-press), the four real dips scaled 1.35× (`margin`, must hold),
  synthetic no-lift re-presses caught (of 1064) and their delay trough→re-press, tap/hold lead.
  - Release side (FastFallSpeed 0–2 × FastFallPercent 0.15–0.3 × FRD 3–20): 2.2.0 is within ~3% of the best
    safe point (0.5/0.25/3: 934 re-presses, +0.6 ms taps). FRD 3 and FastFallSpeed 0 cut out on gentle
    synthetic drags at 500–1000 with ±10–15 noise per sample (`light_*`: 10% dips, 1.5% tremor); 0.5/10
    is the most robust of those; FastFallSpeed 1.5 removes all light cut-outs but loses 4% of re-presses.
    Kept 0.5 / 0.25 / 10.
  - Slow path (Drift 10–200 × Release 400–900): Drift 200 / 600 gives hold ends +8 ms and passes the real-dip
    margin, but the old 300 Hz log's slow hold dips (2000–3100 over 500–800 ms, ~4–6 raw/ms) would lag the
    reference by 800–1200 and release. Kept 10 / 600.
  - Re-press side: the play log has a +39 bump after the release at t ≈ 89.3 s (6976 → 7015 on the way to
    lifting); ActivationDistance ≤ 39 double-clicks there, so the old fixed 40 had a 1.03 margin. Swept
    fixed 0–20 + 0.3–1.0 % of the trough: 20 + 0.5 % has 1.41 margin there, 27.5 at 1500, and re-presses
    0.6 ms sooner on average (11.6 vs 12.2 ms; first-sample bound is ~9). Lower floors gain up to 1.7 ms but
    sit at or below likely resting-pen noise at light pressure (σ unknown; ±15 per 9 ms sample would
    re-press at < 30).
  - `rt calibrate` on the play log (with 68 blanked) recommends FastFallPercent 0.2 / FRD 3.9 / Release 234 /
    Activation 77: its margin scales distances, not dips, it ignores light-pressure noise, and σ = 12.7
    includes hand motion between samples, so it was not adopted. Its fast-distance noise floor now uses the
    allowance at the 10th-percentile pressure (`4σ − (FastFallSpeed + FastFallPercent%·p10)·interval`).

- Hold dips that cut out before: single-sample falls of 84–120 (≤ 13 raw/ms) at 7000–8191, 2-sample ≤ 225,
  total ≤ 490. With 20 raw/ms × 9 ms = 180 allowance + 20, one sample must fall ≥ 200 (67% margin).
- Stroke 68 (t ≈ 67.3 s): 8191 → 6599 in 4 samples (up to 65 raw/ms), back up to 7428, lift 150 ms later.
  As fast as a tap. **Intended** (owner, 2026-10-01): a release + re-press without lifting.
- 2.1.0 vs 2.0.1: hold ends release ~40 ms later and taps ~5 ms later than as recorded. The early
  releases came from the same sensitivity (effectively ~2.2 raw/ms) that cut the hold dips: hold ends start
  with the same 30–120 per-sample falls as those dips (e.g. stroke 44 vs 90, both falling from 8191).
- Sensitivity frontier on this log (FRD/FastFallSpeed sweeps): FastFallSpeed 16 / FRD 15 gives taps 30.0,
  holds 67.9, still only stroke 68, but cuts 11 of the 9 ms-held synthetic drags, so it was not adopted
  without a real drag recording. 12 / 20 is the edge (stroke 79 cuts at 10 / 40 and 12 / 10).
- Allowance proportional to pressure was first dropped in 2.1.0 (only ~+1 ms taps, +3 ms holds); its real gain
  is on re-presses without lifting, which the lifted-only recording could not show. Adopted in 2.2.0.
  Allowance ramping from low (taps) to high (holds) over HoldTime: +2–3 ms taps but thin margin (tap stroke 3
  cuts one step more sensitive) and it is a time-based tap/hold split like TimerwThreshold/HSK.
  HoldReleaseMultiplier on distances: no gain (dips are a speed problem). Slow path with long drift
  (τ 100–200 ms, 500–800): earlier hold ends but cuts most synthetic drags.
- The slow path almost never fires on this tablet: with 9 ms samples and τ 10 ms the reference catches up
  within a sample. It is a safety net only.
- Two one-sample strokes: 8191 for one sample 37 ms after a lift (t ≈ 59.55 s, looks like a firmware/sensor
  glitch) and 431 for one sample (t ≈ 80.26 s). Each becomes a 9 ms click. The 8191 kind is filtered since
  2.3.1 (see below); the 431 one is indistinguishable from a light graze and still clicks.
- **Second recording, other nib** (`recordings/play-ptk670-newtip-20261001.csv`, 231 s, 2.3.0 defaults,
  S4 League melee, not osu!): 231 strokes, 175 taps, 55 holds. Same 9 ms sampling. 8 phantom one-sample 8191
  strokes 11–47 ms after a lift (3.5% of strokes). No real stroke in either log starts ≥ 7000, so 2.3.1 holds
  back contacts that start at 8191 (cost on real contacts: none observed). 12 re-presses inside strokes
  (landing wobble, rebounds during releases, hold dips); the owner did not notice them in game and is fine
  with them. Speed vs the old nib, same settings: tap release detected 21.0 vs 21.8 ms after the descent starts
  (same p50 18 / p90 36: a tie within one sample); holds reach 8191 in 54% vs 28% (release start hidden while
  saturated). Owner went back to the old nib.

**Old 300 Hz log** (`logs/pressure_log_detailed.csv`, see below). Numbers from `rt replay` (before the
per-sample change, which does not affect real 1 kHz or 300 Hz data where every report is a new value):

| | old 0112 settings | defaults |
|---|---|---|
| synthetic drags (`syn_drag`): cut-outs | 60 | 0 |
| synthetic taps (`syn_tap`): release p50 / pressure shed | 7 ms / 447 | 3 ms / 132 |
| real 300 Hz log: missed taps (of 116 estimated) | 0 | 6 |
| real 300 Hz log: release p50 from descent start | 29 ms | 33 ms |

Old 0112 settings for replay: `--set ContactThreshold=4 --set LiftThreshold=9 --set ActivationDistance=4
--set ReleaseDistance=10 --set ReleaseRatio=0.2 --set MaxReleaseDistance=400 --set DriftTimeConstant=0
--set FastFallSpeed=0 --set FastFallPercent=0`. 2.1.0: `--set FastFallSpeed=20 --set FastFallPercent=0
--set FastReleaseDistance=20`.

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
  harsher than real drags. **Real 1000 Hz recordings decide this, via `rt calibrate`.** The 2026-10-01
  recording has holds (≤ 13 raw/ms dips) but no long drags; a `drag-*.csv` recording is still missing.

### Metrics in RtTool (know their limits)

- *Release timing*: ms from the start of the descent (most recent prominent peak; last sample within
  max(3σ, 1%) of it, counted from the report where that sample first appears, not its repeat) to the release,
  plus pressure shed. On slow falls, "start of descent" is fuzzy.
- *Replay timing*: RtTool feeds the engine the `engine_ms` column when the log has it (newer diagnostics: the time
  the driver's engine was given, including the Firmware Sample Clock), otherwise `t_ms`, capped at 50 ms per row
  like the filter, so replay = driver.
- *Cut-outs* (drag logs): a Release/FastRelease followed by a re-press before pressure reaches the lift
  threshold, or no lift within `--lift-window` (300 ms). Meaningful when every stroke is one press (drags,
  or `play-*` logs where the pen lifts after each tap/hold).
- *Lead before lift*: ms from each stroke-ending release to pressure ≤ lift threshold (larger = earlier).
  Label-free latency metric for logs where strokes end in a lift. Quantized to the 9 ms sample interval.
- *Pressure sample interval*: median time between pressure changes while pressed (2 ms on the PTK-670 since
  2026-10-14, 5 ms since 2026-10-05, 9 ms before). On host-timed logs it is a median of jittered 0.2–4 ms gaps.
- *Missed releases* (tap logs): zigzag swings of ≥ max(400, 35% of peak) whose peak was pressed but which
  never released before the trough. A heuristic: on drag logs it counts dips as "taps".
- *Noise σ*: 1.4826·median|2nd difference|/√6 over samples > 50, repeated values skipped. On sample-and-hold
  logs this includes hand motion between samples (12.7 on the PTK-670 play log, so it recommends
  Activation ≥ 77; not adopted since Rearm is untested there). Calibrate recommends Activation ≥ 6σ,
  slow-path distance ≥ 10σ and fast distance ≥ 4σ − (FastFallSpeed + FastFallPercent%·p10)·sample interval,
  p10 = 10th percentile of pressures > 50.
- The calibrator scales ReleaseDistance, ReleaseRatio, MaxReleaseDistance and FastReleaseDistance together
  (bisection to the smallest scale with zero cut-outs) for each DriftTimeConstant × FastFallPercent pair
  (`--fast-percents`, 0 = fast detector off; FastFallSpeed comes from `--set`),
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

## Angle-Preserving Sensitivity (separate plugin, 1.0.0, shipped with RT 2.4.0)

The owner plays S4 League (3D melee) in relative mode (VMulti Relative on Windows, Relative Mode on Linux)
with X:Y sensitivity 28.58895 : 20.617332 px/mm (726 : 524 DPI): vertical strokes are finger-only, so a
lower Y trades range for precision and keeps them off the pitch limit. Drawback the owner wanted fixed:
per-axis scaling bends directions (45° → 35.8°). Verified in OTD v0.6.7 `RelativeOutputMode.Transform`:
after the transform, the report position is the per-report delta in px (rotation and sensitivity
applied), so a PostTransform filter can rescale it statelessly. `RelativeMode` (Linux) derives from
`RelativeOutputMode`; the VMulti relative mode source was not reachable from the cloud session but it shows
the same relative settings, so it is assumed to derive from it too.
Math (`DirectionalScale.Apply`): `delta · sqrt((dx² + r²·dy²) / (dx² + dy²))`, r = Vertical Speed / 100:
direction unchanged, length equal to what X:Y = 1 : r would give. Tested in `DirectionalScaleTests`.
Not done: vertical-only acceleration (owner did not ask), circle speed variation (inherent to any r ≠ 1).
Position updates every report (1000 Hz, owner-confirmed), unlike pressure. At 28.59 px/mm one tablet unit is
~0.12 px (~235 units/mm), so per-report deltas at slow aim speeds are a few units and their direction is coarse;
there the plugin degrades towards the plain per-axis ratio (it cannot do worse than it).

## Ideas not yet explored

- Calibrate the fast detector independently of the slow path (currently scaled together).
- Calibrate needs drag logs. `play-*` logs only work as drag input without intended re-presses (the 2026-10-01
  log has one at 67 s, so calibrate calls every fast-detector setting "unstable" on it).
- Press side: a CUSUM on rises could allow a smaller ActivationDistance (gain is sub-ms at 1000 Hz).
- `HoverDistance` (IntuosV3 byte 13) could hint at an imminent contact, but any prediction risks false presses.
- Per-pressure-band thresholds if real drags show noise growing with force (on the play log a pressure-
  proportional fall allowance gained only ~1 ms; see above).
- Recalibrate for the 500 Hz firmware: labelled drag/tap recordings (with Firmware Sample Clock on, so `engine_ms`
  carries the counter timing), then `rt calibrate` and `tools/Sweep` (whose synthetic signals default to 5 ms
  sampling: run with `--ms=2`). Decide whether Firmware Sample Clock should become the default for this firmware.
- If the firmware starts sampling pressure every ms, the 2.2.0 defaults are far too sensitive (synthetic 1 kHz
  drags: 238 cut-outs); FastFallSpeed 20 / FastFallPercent 0 / FastReleaseDistance 40 was fine there. `rt replay` prints the sample interval.

---

## Git-ignored material (exists only on the owner's Linux machine)

### `otd-rt-old/`: the previous plugin and its discarded variants

Forked from Kuuuube/Rapid_Trigger. It contains `.modules/OpenTabletDriver-0.6.x` (an 0.6.x snapshot
versioned 0.6.6.2, what the old csproj referenced) and `.modules/OpenTabletDriver-master`. For current
OTD source, clone https://github.com/OpenTabletDriver/OpenTabletDriver (tags v0.6.7, branches 0.6.x/master). The variants are full `.cs` files
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
