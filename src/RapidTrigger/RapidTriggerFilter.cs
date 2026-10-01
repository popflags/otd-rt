using System;
using System.Diagnostics;
using System.IO;
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
        private uint _maxPressure = 8191;
        private long _lastTicks;

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
            if (value is ITabletReport report)
            {
                _engine ??= CreateEngine();

                long now = Stopwatch.GetTimestamp();
                double elapsed = _lastTicks == 0 ? 0 : Math.Min((now - _lastTicks) * TickToMs, MaxElapsedMs);
                _lastTicks = now;

                uint raw = report.Pressure;
                var ev = _engine.Update(raw, elapsed);

                if (_engine.Pressed)
                    report.Pressure = PreservePressure ? Math.Max(raw, 1u) : _maxPressure;
                else
                    report.Pressure = 0;

                _recorder?.Record(now, raw, _engine, ev);
            }
            else if (value is OutOfRangeReport)
            {
                _engine?.Reset();
            }

            Emit?.Invoke(value);
        }

        private TriggerEngine CreateEngine()
        {
            var settings = new TriggerSettings
            {
                ContactThreshold = ContactThreshold,
                LiftThreshold = LiftThreshold,
                ActivationDistance = ActivationDistance,
                ReleaseDistance = ReleaseDistance,
                ReleaseRatio = ReleaseRatio,
                MaxReleaseDistance = MaxReleaseDistance,
                DriftTimeConstant = DriftTimeConstant,
                FastFallSpeed = FastFallSpeed,
                FastFallPercent = FastFallPercent,
                FastReleaseDistance = FastReleaseDistance,
                PressDriftTimeConstant = PressDriftTimeConstant,
                HoldTime = HoldTime,
                HoldReleaseMultiplier = HoldReleaseMultiplier,
            };

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
            _recorder?.Dispose();
            _recorder = null;
        }

        [Property("Contact Threshold"), DefaultPropertyValue(4.0), Unit("raw"), ToolTip(
            "Pressure that presses the tip when the pen comes down from the air (first report at or above it).\n" +
            "Lower = earlier first press. Must stay above any pressure the pen reports while hovering.")]
        public double ContactThreshold { set; get; } = 4;

        [Property("Lift Threshold"), DefaultPropertyValue(2.0), Unit("raw"), ToolTip(
            "At or below this pressure the tip is always released, and the next press is treated as a fresh contact.")]
        public double LiftThreshold { set; get; } = 2;

        [Property("Activation Distance"), DefaultPropertyValue(40.0), Unit("raw"), ToolTip(
            "Rapid re-press: how far pressure must rise above its lowest point since the last release.\n" +
            "Keep it above the pressure noise (rt replay prints a noise estimate).")]
        public double ActivationDistance { set; get; } = 40;

        [Property("Fast Fall Speed"), DefaultPropertyValue(0.5), Unit("raw/ms"), ToolTip(
            "Fast release detector: fixed part of the drag allowance. Falls slower than\n" +
            "Fast Fall Speed + Fast Fall Percent of the pressure are what a drag or hold can do; only the part of each\n" +
            "pressure sample's fall above it counts towards Fast Release Distance.\n" +
            "Higher = steadier drags, later releases. Both 0 = detector off.")]
        public double FastFallSpeed { set; get; } = 0.5;

        [Property("Fast Fall Percent"), DefaultPropertyValue(0.25), Unit("%/ms"), ToolTip(
            "Fast release detector: part of the drag allowance that grows with pressure (hand wobble grows with force).\n" +
            "Higher = steadier heavy holds, later releases and missed shallow re-presses without lifting.")]
        public double FastFallPercent { set; get; } = 0.25;

        [Property("Fast Release Distance"), DefaultPropertyValue(10.0), Unit("raw"), ToolTip(
            "Fast release detector: release once the fall in excess of the drag allowance adds up to this.\n" +
            "Lower = earlier releases. Keep it above the pressure noise (>= 40 if pressure changes every report).")]
        public double FastReleaseDistance { set; get; } = 10;

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

        [Property("Hold Time"), DefaultPropertyValue(150.0), Unit("ms"), ToolTip(
            "Time over which both release distances ramp up to the Hold Release Multiplier.")]
        public double HoldTime { set; get; } = 150;

        [Property("Hold Release Multiplier"), DefaultPropertyValue(1.0), ToolTip(
            "Release distance multiplier for long presses (drags/holds). Taps stay fast. 1 = off.")]
        public double HoldReleaseMultiplier { set; get; } = 1;

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
