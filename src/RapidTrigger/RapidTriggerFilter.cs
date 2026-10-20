using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace RapidTrigger
{
    [PluginName("Rapid Trigger")]
    public sealed class RapidTriggerFilter : IPositionedPipelineElement<IDeviceReport>, IDisposable
    {
        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

        // Reports further apart than this (pen re-entering range, driver hiccup) are not treated as elapsed hold time.
        private const double MaxElapsedMs = 50;

        private TriggerEngine? _engine;
        private DiagnosticsRecorder? _recorder;
        private FirmwareSampleClock _sampleClock = new();
        private bool _mismatchLogged;
        private int _lastButtons = -1;
        private uint _maxPressure = 8191;
        private long _lastTicks;

        // Sum of the elapsed times given to the engine (recorded so a replay sees the same timing).
        private double _engineMs;

        // Touchdown Gate: a click it holds back goes out on a later report, or from this timer when no report arrives in time
        // (pen out of range). Consume and the timer then share a lock, like OpenTabletDriver's own async filters; the timer
        // re-emits the last report with its original position. Null while the gate is off.
        private readonly object _sync = new();
        private Timer? _gateTimer;
        private bool _disposed;
        private long _hostTicks;
        private long _armedTicks;
        private ITabletReport? _lastReport;
        private Vector2 _lastPosition;
        private uint _lastRaw;

        public event Action<IDeviceReport>? Emit;

        // First in the chain: no other filter can alter or delay pressure before the decision is made.
        public PipelinePosition Position => PipelinePosition.PreTransform;

        [TabletReference]
        public TabletReference Tablet
        {
            set
            {
                uint? max = value?.Properties?.Specifications?.Pen?.MaxPressure;
                if (max is > 0)
                    _maxPressure = max.Value;
            }
        }

        public void Consume(IDeviceReport value)
        {
            if (TouchdownGate > 0)
            {
                lock (_sync)
                    ConsumeReport(value);
            }
            else
            {
                ConsumeReport(value);
            }
        }

        private void ConsumeReport(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                _engine ??= CreateEngine();

                long now = Stopwatch.GetTimestamp();
                double elapsed = _lastTicks == 0 ? 0 : Math.Min((now - _lastTicks) * TickToMs, MaxElapsedMs);
                _lastTicks = now;
                if (UseFirmwareSampleClock)
                    elapsed = SampleClockElapsed(value, elapsed);
                _engineMs += elapsed;
                double hostElapsed = _hostTicks == 0 ? 0 : (now - _hostTicks) * TickToMs;
                _hostTicks = now;

                // Side buttons of the PTK firmware's 0x1E reports; the engine only uses their changes with Analog Pen on.
                int buttons = FirmwareSampleClock.PenButtons(value.Raw);
                bool buttonsChanged = buttons >= 0 && _lastButtons >= 0 && buttons != _lastButtons;
                _lastButtons = buttons;

                uint raw = report.Pressure;
                var ev = _engine.Update(raw, elapsed, hostElapsed, buttonsChanged);
                report.Pressure = OutputPressure(raw);

                _recorder?.Record(now, _engineMs, raw, _engine, ev, buttons, UseFirmwareSampleClock && _sampleClock.Gap);

                if (_gateTimer != null)
                {
                    _lastReport = report;
                    _lastPosition = report.Position;
                    _lastRaw = raw;
                    ArmGateTimer(now);
                }
            }
            else if (value is OutOfRangeReport)
            {
                _engine?.Reset();
                _sampleClock.Reset();
                _lastButtons = -1;
            }

            Emit?.Invoke(value);
        }

        private uint OutputPressure(uint raw)
            => _engine!.OutputPressed ? (PreservePressure ? Math.Max(raw, 1u) : _maxPressure) : 0;

        // One-shot fallback a little after a held-back click is due: reports normally get there first and the timer finds
        // nothing to do. Re-armed only when the due time moves.
        private void ArmGateTimer(long now)
        {
            double due = _engine!.OutputDueInMs;
            if (due < 0)
            {
                if (_armedTicks != 0)
                {
                    _gateTimer!.Change(Timeout.Infinite, Timeout.Infinite);
                    _armedTicks = 0;
                }
                return;
            }
            long target = now + (long)(due / TickToMs);
            if (_armedTicks != 0 && Math.Abs(target - _armedTicks) * TickToMs < 0.5)
                return;
            _armedTicks = target;
            _gateTimer!.Change((long)Math.Ceiling(due) + 1, Timeout.Infinite);
        }

        private void OnGateTimer(object? state)
        {
            lock (_sync)
            {
                _armedTicks = 0;
                if (_disposed || _engine == null || _lastReport == null)
                    return;

                long now = Stopwatch.GetTimestamp();
                double hostElapsed = (now - _hostTicks) * TickToMs;
                _hostTicks = now;
                var ev = _engine.Tick(hostElapsed);
                if (ev != TriggerEvent.None)
                {
                    // Filters after this one and the output mode changed the last report in place; restore what came in.
                    _lastReport.Position = _lastPosition;
                    _lastReport.Pressure = OutputPressure(_lastRaw);
                    _recorder?.Record(now, _engineMs, _lastRaw, _engine, ev, _lastButtons, false);
                    Emit?.Invoke(_lastReport);
                }
                ArmGateTimer(now);
            }
        }

        private double SampleClockElapsed(IDeviceReport report, double hostElapsed)
        {
            if (_sampleClock.TryElapsed(report.Raw, hostElapsed, MaxElapsedMs, out double elapsed))
            {
                _mismatchLogged = false;
                return Math.Min(elapsed, MaxElapsedMs);
            }

            if (_sampleClock.Mismatch && !_mismatchLogged)
            {
                _mismatchLogged = true;
                Log.Write("Rapid Trigger", "Firmware Sample Clock disagrees with the host clock (firmware changed, or Analog Pen does not " +
                    "match the pen?); using host time until the pen changes.", LogLevel.Warning);
            }
            return hostElapsed;
        }

        private TriggerEngine CreateEngine()
        {
            var settings = new TriggerSettings
            {
                ContactThreshold = ContactThreshold,
                PhantomContactPressure = PhantomContactPressure,
                PhantomConfirmTime = PhantomConfirmTime,
                DropoutTime = DropoutTime,
                DropoutPressure = DropoutPressure,
                GlitchJump = GlitchJump,
                LiftThreshold = LiftThreshold,
                ActivationDistance = ActivationDistance,
                ActivationPercent = ActivationPercent,
                ReleaseDistance = ReleaseDistance,
                ReleaseRatio = ReleaseRatio,
                MaxReleaseDistance = MaxReleaseDistance,
                DriftTimeConstant = DriftTimeConstant,
                FastFallSpeed = FastFallSpeed,
                FastFallPercent = FastFallPercent,
                FastReleaseDistance = FastReleaseDistance,
                TapAllowance = TapAllowance,
                RepressAllowance = RepressAllowance,
                PressHoldoff = PressHoldoff,
                MinimumClick = MinimumClick,
                PressDriftTimeConstant = PressDriftTimeConstant,
                HoldTime = HoldTime,
                HoldReleaseMultiplier = HoldReleaseMultiplier,
                HoldAllowance = HoldAllowance,
                ReboundDistance = ReboundDistance,
                TouchdownGate = TouchdownGate,
                AnalogPen = AnalogPen,
                ButtonSettleReports = ButtonSettleReports,
            };
            _sampleClock = new FirmwareSampleClock(AnalogPen);

            if (TouchdownGate > 0)
                _gateTimer = new Timer(OnGateTimer, null, Timeout.Infinite, Timeout.Infinite);

            if (EnableDiagnostics)
            {
                try
                {
                    var directory = string.IsNullOrWhiteSpace(DiagnosticsDirectory)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "rapid-trigger-logs")
                        : DiagnosticsDirectory;
                    _recorder = new DiagnosticsRecorder(directory);
                    Log.Write("Rapid Trigger", $"Recording diagnostics to '{_recorder.FilePath}'");
                }
                catch (Exception e)
                {
                    Log.Write("Rapid Trigger", $"Diagnostics disabled: {e.Message}", LogLevel.Warning);
                }
            }

            return new TriggerEngine(settings);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _gateTimer?.Dispose();
                _gateTimer = null;
            }
            _recorder?.Dispose();
            _recorder = null;
        }

        [Property("Contact Threshold"), DefaultPropertyValue(4.0), Unit("raw"), ToolTip(
            "Pressure that presses the tip when the pen comes down from the air (first report at or above it).\n" +
            "Lower = earlier first press. Must stay above any pressure the pen reports while hovering.")]
        public double ContactThreshold { set; get; } = 4;

        [Property("Phantom Contact Pressure"), DefaultPropertyValue(8191.0), Unit("raw"), ToolTip(
            "A contact whose first report is at or above this pressure must last Phantom Confirm Time before it presses.\n" +
            "Filters the single max-pressure samples the PTK-670 sometimes reports just after a lift. 0 = off.")]
        public double PhantomContactPressure { set; get; } = 8191;

        [Property("Phantom Confirm Time"), DefaultPropertyValue(20.0), Unit("ms"), ToolTip(
            "How long a contact that starts at Phantom Contact Pressure must last before it presses.")]
        public double PhantomConfirmTime { set; get; } = 20;

        [Property("Dropout Time"), DefaultPropertyValue(60.0), Unit("ms"), ToolTip(
            "A drop straight from Dropout Pressure or more to zero is held back this long. If pressure comes back in\n" +
            "that time, the zero is ignored as a sensor dropout (the PTK-670 reports 0 for 5-45 ms when pressed hard).\n" +
            "Real lifts pass through lower pressures first and are not delayed. 0 = off.")]
        public double DropoutTime { set; get; } = 60;

        [Property("Dropout Pressure"), DefaultPropertyValue(6000.0), Unit("raw"), ToolTip(
            "Lowest pressure a drop to zero can come from and still be treated as a possible dropout.\n" +
            "Dropouts were seen from 7750 up; real lifts came from 3000 or less.")]
        public double DropoutPressure { set; get; } = 6000;

        [Property("Glitch Jump"), DefaultPropertyValue(0.0), Unit("raw"), ToolTip(
            "A single sample that jumps at least this far (up or down) is held back for up to Dropout Time, and ignored\n" +
            "if pressure comes back within this distance in that time (one-sample sensor glitches while pressed hard:\n" +
            "8191 -> 692 -> 8191). Real changes stay under ~1800 per 2 ms sample. 2500 for the PTK-670 at 2 ms. 0 = off.")]
        public double GlitchJump { set; get; }

        [Property("Lift Threshold"), DefaultPropertyValue(2.0), Unit("raw"), ToolTip(
            "At or below this pressure the tip is always released, and the next press is treated as a fresh contact.")]
        public double LiftThreshold { set; get; } = 2;

        [Property("Activation Distance"), DefaultPropertyValue(10.0), Unit("raw"), ToolTip(
            "Rapid re-press: how far pressure must rise above its lowest point since the last release (fixed part).\n" +
            "Keep the total above the pressure noise. Lower = earlier re-presses, more risk of double clicks.")]
        public double ActivationDistance { set; get; } = 10;

        [Property("Activation Percent"), DefaultPropertyValue(0.5), Unit("%"), ToolTip(
            "Rapid re-press: part of the required rise that grows with pressure, in % of the lowest point.\n" +
            "Keeps wobble on heavy releases from re-pressing while light re-presses stay quick.")]
        public double ActivationPercent { set; get; } = 0.5;

        [Property("Fast Fall Speed"), DefaultPropertyValue(2.0), Unit("raw/ms"), ToolTip(
            "Fast release detector: fixed part of the drag allowance. Falls slower than\n" +
            "Fast Fall Speed + Fast Fall Percent of the pressure are what a drag or hold can do; only the part of each\n" +
            "pressure sample's fall above it counts towards Fast Release Distance.\n" +
            "Higher = steadier drags, later releases. Both 0 = detector off.")]
        public double FastFallSpeed { set; get; } = 2;

        [Property("Fast Fall Percent"), DefaultPropertyValue(0.19), Unit("%/ms"), ToolTip(
            "Fast release detector: part of the drag allowance that grows with pressure (hand wobble grows with force).\n" +
            "Higher = steadier heavy holds, later releases and missed shallow re-presses without lifting.")]
        public double FastFallPercent { set; get; } = 0.19;

        [Property("Fast Release Distance"), DefaultPropertyValue(10.0), Unit("raw"), ToolTip(
            "Fast release detector: release once the fall in excess of the drag allowance adds up to this.\n" +
            "Lower = earlier releases. Keep it above the pressure noise (>= 40 if pressure changes every report).")]
        public double FastReleaseDistance { set; get; } = 10;

        [Property("Tap Allowance"), DefaultPropertyValue(0.4), ToolTip(
            "Fast release detector: after a contact from the air the drag allowance starts at this fraction and ramps to\n" +
            "the full allowance over Hold Time. Taps release earlier; holds and drags get the full allowance once they last.\n" +
            "Lower = faster taps, less margin for dips early in a hold or drag (0.15 = ~1.5 ms faster, riskier). 1 = off.")]
        public double TapAllowance { set; get; } = 0.4;

        [Property("Re-press Allowance"), DefaultPropertyValue(0.5), ToolTip(
            "Same ramp after a re-press without lifting. Milder, because a re-press inside a held motion usually continues\n" +
            "as a hold. 1 = off.")]
        public double RepressAllowance { set; get; } = 0.5;

        [Property("Hold Allowance"), DefaultPropertyValue(1.0), ToolTip(
            "Fast release detector: the drag allowance is multiplied by this once a press has lasted Hold Time (drags and\n" +
            "holds); taps are not affected. For setups without re-presses (Activation Distance huge), where a release cannot be\n" +
            "undone until the pen lifts: drag ends that dip and then hold stop releasing, real lifts still do (~20 ms later).\n" +
            "4 with Release Distance 1000 removed almost all such cut-outs. With re-presses on it swallows re-clicks in drags. 1 = off.")]
        public double HoldAllowance { set; get; } = 1;

        [Property("Rebound Distance"), DefaultPropertyValue(0.0), Unit("raw"), ToolTip(
            "A release held back by Press Holdoff is dropped as a landing bounce once pressure is back up by this much and rising.\n" +
            "0 = the re-press distance (Activation Distance + Percent). Set it (10) when Activation Distance is huge, otherwise\n" +
            "the bounce rule never triggers and landing dips release for good.")]
        public double ReboundDistance { set; get; }

        [Property("Press Holdoff"), DefaultPropertyValue(8.0), Unit("ms"), ToolTip(
            "For this long after a press only a lift releases. Absorbs landing bounces right after contact that would\n" +
            "otherwise release and re-press (double click). 0 = off.")]
        public double PressHoldoff { set; get; } = 8;

        [Property("Minimum Click"), DefaultPropertyValue(0.0), Unit("ms"), ToolTip(
            "Every click lasts at least this long; releases (lifts included) wait until then. Rapid releases make quick\n" +
            "taps very short clicks, and a game that reads the button once per frame can miss a click shorter than a\n" +
            "frame (60 fps: 16.7 ms). Applies to taps, brushes and re-presses alike. Keep it under ~50 ms. 0 = off.")]
        public double MinimumClick { set; get; }

        [Property("Touchdown Gate"), DefaultPropertyValue(500.0), Unit("ms"), ToolTip(
            "Experimental. A touchdown (pen coming down from the air) clicks no sooner than this long after the previous\n" +
            "touchdown's click; one that lands earlier clicks exactly then. Taps and drags alike (they cannot be told apart\n" +
            "at touchdown). Counts from the previous click, so tapping faster than this falls behind. Re-presses without\n" +
            "lifting and releases are not delayed; a tap that already ended clicks for as long as it lasted. 0 = off.")]
        public double TouchdownGate { set; get; } = 500;

        [Property("Release Distance"), DefaultPropertyValue(600.0), Unit("raw"), ToolTip(
            "Slow path: pressure fall below the hold reference that releases at any speed.\n" +
            "Catches deliberate releases that are slower than Fast Fall Speed.")]
        public double ReleaseDistance { set; get; } = 600;

        [Property("Release Ratio"), DefaultPropertyValue(0.0), ToolTip(
            "Slow path: release distance as a fraction of the hold reference.\n" +
            "The larger of this and Release Distance is used. 0 = fixed distance only.")]
        public double ReleaseRatio { set; get; }

        [Property("Max Release Distance"), DefaultPropertyValue(1000.0), Unit("raw"), ToolTip(
            "Cap for the ratio-based release distance.")]
        public double MaxReleaseDistance { set; get; } = 1000;

        [Property("Drift Time Constant"), DefaultPropertyValue(10.0), Unit("ms"), ToolTip(
            "Slow path drag stability: the hold reference follows slow pressure changes with this time constant,\n" +
            "so gradual easing off during a drag never adds up to a release. It is frozen during fast falls.\n" +
            "Lower = steadier drags. 0 = classic rapid trigger (reference stays at the peak).")]
        public double DriftTimeConstant { set; get; } = 10;

        [Property("Press Drift Time Constant"), DefaultPropertyValue(0.0), Unit("ms"), ToolTip(
            "Same as Drift Time Constant on the press side: ignores slow pressure creep while released.\n" +
            "0 = off (fastest re-press).")]
        public double PressDriftTimeConstant { set; get; } = 0;

        [Property("Hold Time"), DefaultPropertyValue(250.0), Unit("ms"), ToolTip(
            "Time over which Tap / Re-press Allowance ramp to the full allowance and both release distances to the\n" +
            "Hold Release Multiplier.")]
        public double HoldTime { set; get; } = 250;

        [Property("Hold Release Multiplier"), DefaultPropertyValue(1.0), ToolTip(
            "Release distance multiplier for long presses (drags/holds). Taps stay fast. 1 = off.")]
        public double HoldReleaseMultiplier { set; get; } = 1;

        [BooleanProperty("Firmware Sample Clock", ""), DefaultPropertyValue(false), ToolTip(
            "PTK-670 custom firmware only (0x1E reports, 1000 Hz motion sync ON: pressure every 2.000 ms, counter in byte 17).\n" +
            "Times pressure samples by the tablet's counter instead of when the reports reach the PC, which arrive in\n" +
            "bursts (samples 0.2-4 ms apart instead of 2). Falls back to host time for other reports, and until the pen\n" +
            "changes if the counter stops matching the host clock (logged as a warning).")]
        public bool UseFirmwareSampleClock { set; get; }

        [BooleanProperty("Analog Pen (CP923)", ""), DefaultPropertyValue(false), ToolTip(
            "PTK-670 custom firmware with the analog pen (CP923 / Wacom One pen): a new pressure sample on every report.\n" +
            "Firmware Sample Clock then counts 1.000 ms per counter step and adds the report gaps of side-button changes.\n" +
            "A side-button change steps the pressure (up to ~400 over a few reports): it is taken as the new level, not as\n" +
            "a release or re-press (see Button Settle Reports). The phantom check is off (this pen makes none).\n" +
            "Leave off for the Pro Pen 3: its phantom max-pressure contacts would click.")]
        public bool AnalogPen { set; get; }

        [Property("Button Settle Reports"), DefaultPropertyValue(10.0), ToolTip(
            "Analog Pen only: for this many reports after a side-button change, pressure changes are taken as the new level\n" +
            "(the firmware ramps the pressure for ~5 reports, then it overshoots for a few more). Lifts still release at once;\n" +
            "a real release in that time is decided when it ends (up to this many ms later). 0 = only the report with the change.")]
        public double ButtonSettleReports { set; get; } = 10;

        [BooleanProperty("Preserve Pressure", ""), DefaultPropertyValue(false), ToolTip(
            "Off: while pressed, report full pressure so the tip binding fires on the very first pressed report.\n" +
            "Keep the pen tip threshold at 0% either way (newer OpenTabletDriver versions apply it before filters).\n" +
            "On: pass the real pressure through (minimum 1). Set the pen tip threshold to 0% in the bindings\n" +
            "tab, otherwise presses are delayed until pressure exceeds it.")]
        public bool PreservePressure { set; get; }

        [BooleanProperty("Enable Diagnostics", ""), DefaultPropertyValue(false), ToolTip(
            "Record every report to a CSV for replay and calibration with RtTool.\n" +
            "File writing happens on a background thread.")]
        public bool EnableDiagnostics { set; get; }

        [Property("Diagnostics Directory"), DefaultPropertyValue(""), ToolTip(
            "Where diagnostics CSVs are written. Empty = ~/rapid-trigger-logs")]
        public string DiagnosticsDirectory { set; get; } = string.Empty;
    }
}
