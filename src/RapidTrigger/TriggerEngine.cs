using System;

namespace RapidTrigger
{
    public enum TriggerEvent : byte
    {
        None = 0,

        /// <summary>Pressed from a lifted pen (pressure was at or below the lift threshold).</summary>
        Contact = 1,

        /// <summary>Pressed again without lifting: pressure rose by the activation distance above its lowest point.</summary>
        Rearm = 2,

        /// <summary>Released without lifting: pressure fell by the release distance below the hold reference.</summary>
        Release = 3,

        /// <summary>Released because pressure reached the lift threshold.</summary>
        Lift = 4,

        /// <summary>Released by the fast-fall detector: pressure fell faster than any drag does, for long enough.</summary>
        FastRelease = 5,
    }

    /// <summary>
    /// All values are in raw tablet pressure units (0..MaxPressure) or milliseconds.
    /// </summary>
    public sealed class TriggerSettings
    {
        /// <summary>Pressure that presses the tip when coming from a lifted pen. Lowest = fastest first press.</summary>
        public double ContactThreshold { get; set; } = 4;

        /// <summary>
        /// A contact whose first report is at or above this pressure waits PhantomConfirmTime before pressing, and is
        /// dropped if the pen lifts in the meantime. The PTK-670 sometimes reports a single sample at exactly max
        /// pressure (8191) ~40 ms after a lift; no real contact in the recordings started above 7000. 0 = off.
        /// </summary>
        public double PhantomContactPressure { get; set; } = 8191;

        /// <summary>How long a contact that starts at PhantomContactPressure must last before it presses, in ms.</summary>
        public double PhantomConfirmTime { get; set; } = 20;

        /// <summary>At or below this the tip is always released, and the next press counts as a fresh contact.</summary>
        public double LiftThreshold { get; set; } = 2;

        /// <summary>
        /// Rise above the lowest pressure since the last release that presses again without lifting
        /// (fixed part; ActivationPercent adds a part that grows with that lowest pressure).
        /// </summary>
        public double ActivationDistance { get; set; } = 20;

        /// <summary>
        /// Part of the re-press rise that grows with pressure, in % of the lowest pressure since the release. Wobble
        /// grows with force: on the PTK-670 recording a release from a 7000 hold bumped back up by 39 before lifting.
        /// </summary>
        public double ActivationPercent { get; set; } = 0.5;

        /// <summary>
        /// Fast-fall detector, drag allowance: falls slower than FastFallSpeed + FastFallPercent% of the pressure
        /// (raw units per ms) are what a drag or hold can do. Only the part of each pressure sample's fall above the
        /// allowance (times the time since the previous sample) counts towards FastReleaseDistance.
        /// Both 0 = detector off.
        /// </summary>
        public double FastFallSpeed { get; set; } = 0.5;

        /// <summary>
        /// Fast-fall detector: the part of the drag allowance that grows with pressure, in % of the current pressure
        /// per ms. Hand wobble grows with force: on the PTK-670 recording, hold dips at 7000-8191 fell up to
        /// 13 raw/ms, while re-presses without lifting at lighter pressure need a smaller allowance to release.
        /// </summary>
        public double FastFallPercent { get; set; } = 0.25;

        /// <summary>
        /// Fast-fall detector: release once the fall in excess of the allowance adds up to this (a one-sided CUSUM).
        /// Smaller = earlier tap releases; must stay above what sensor noise can add up to.
        /// </summary>
        public double FastReleaseDistance { get; set; } = 10;

        /// <summary>Slow path: fall below the hold reference that releases, whatever the speed.</summary>
        public double ReleaseDistance { get; set; } = 600;

        /// <summary>Slow path: fall as a fraction of the hold reference. The larger of this and ReleaseDistance is used.</summary>
        public double ReleaseRatio { get; set; } = 0;

        /// <summary>Upper bound for the proportional release distance.</summary>
        public double MaxReleaseDistance { get; set; } = 1000;

        /// <summary>
        /// Slow path: time constant of the hold reference. The reference jumps up to new peaks instantly and drifts
        /// down towards the current pressure with this time constant, so slow pressure changes during a drag are
        /// absorbed. It does not drift while a fast fall is building up. 0 = never drifts (classic rapid trigger).
        /// </summary>
        public double DriftTimeConstant { get; set; } = 10;

        /// <summary>Same idea on the press side: the trough creeps up after slow rises. 0 = off (fastest re-press).</summary>
        public double PressDriftTimeConstant { get; set; } = 0;

        /// <summary>Time over which the release distances ramp up to HoldReleaseMultiplier while pressed.</summary>
        public double HoldTime { get; set; } = 150;

        /// <summary>Release distance multiplier (both paths) reached after HoldTime. 1 = off.</summary>
        public double HoldReleaseMultiplier { get; set; } = 1;

        public TriggerSettings Clone() => (TriggerSettings)MemberwiseClone();
    }

    /// <summary>
    /// Rapid trigger state machine. Call <see cref="Update"/> once per tablet report with the raw pressure and the
    /// time since the previous report. Allocation-free.
    /// <para>
    /// While pressed, two detectors run side by side:
    /// a fast-fall detector (one-sided CUSUM on the per-report fall, minus a drag speed allowance) that releases
    /// taps as soon as the fall is provably faster than a drag, and a slow path (fall below a drifting hold
    /// reference) that catches slower deliberate releases while absorbing slow drag dips.
    /// </para>
    /// </summary>
    public sealed class TriggerEngine
    {
        private readonly TriggerSettings _settings;

        // Pressed: hold reference (drifting peak). Released: trough (lowest pressure since release).
        private double _anchor;
        private double _previousPressure;
        private double _sinceChangeMs;
        private bool _fresh = true;

        // Time a suspected phantom contact has lasted; negative = none pending.
        private double _phantomMs = -1;

        public TriggerEngine(TriggerSettings settings)
        {
            _settings = settings.Clone();
        }

        public TriggerSettings Settings => _settings.Clone();

        public bool Pressed { get; private set; }

        /// <summary>Hold reference while pressed, trough while released.</summary>
        public double Anchor => _anchor;

        /// <summary>Slow-path release distance on the last pressed report (kept after a release for diagnostics).</summary>
        public double CurrentReleaseDistance { get; private set; }

        /// <summary>Hold reference on the last pressed report (kept after a release for diagnostics).</summary>
        public double HoldReference { get; private set; }

        /// <summary>Fast-fall detector state: accumulated fall in excess of the drag allowance.</summary>
        public double FallExcess { get; private set; }

        /// <summary>Time since the current press started, in milliseconds.</summary>
        public double HeldTime { get; private set; }

        public void Reset()
        {
            Pressed = false;
            _anchor = 0;
            _previousPressure = 0;
            _sinceChangeMs = 0;
            _fresh = true;
            _phantomMs = -1;
            CurrentReleaseDistance = 0;
            HoldReference = 0;
            FallExcess = 0;
            HeldTime = 0;
        }

        public TriggerEvent Update(uint pressure, double elapsedMs)
        {
            if (!(elapsedMs > 0))
                elapsedMs = 0;

            double p = pressure;
            double previous = _previousPressure;
            _previousPressure = p;

            // Some tablets sample pressure slower than they send reports and repeat the last value in between
            // (the PTK-670 on 1000 Hz firmware: a new pressure sample every ~9 reports). A change then covers all the
            // time since the previous change, not just the last report interval.
            _sinceChangeMs += elapsedMs;
            double sampleMs = _sinceChangeMs;
            if (p != previous)
                _sinceChangeMs = 0;

            return Pressed ? UpdatePressed(p, previous, elapsedMs, sampleMs) : UpdateReleased(p, previous, elapsedMs);
        }

        private TriggerEvent UpdateReleased(double p, double previous, double elapsedMs)
        {
            var s = _settings;

            if (p <= s.LiftThreshold)
                _fresh = true;

            // The trough drifts towards the previous sample over the elapsed interval, then snaps down to new lows.
            if (s.PressDriftTimeConstant > 0 && previous > _anchor)
                _anchor += (previous - _anchor) * Follow(elapsedMs, s.PressDriftTimeConstant);
            if (p < _anchor)
                _anchor = p;

            if (_fresh)
            {
                if (!(p >= s.ContactThreshold && p > s.LiftThreshold))
                {
                    _phantomMs = -1;
                    return TriggerEvent.None;
                }

                // A contact that starts at max pressure is held back until it has lasted long enough to be real.
                // It presses at once if pressure moves below the phantom level, and is dropped if the pen lifts.
                if (s.PhantomContactPressure > 0 && p >= s.PhantomContactPressure)
                {
                    _phantomMs = _phantomMs < 0 ? 0 : _phantomMs + elapsedMs;
                    if (_phantomMs < s.PhantomConfirmTime)
                        return TriggerEvent.None;
                }

                _phantomMs = -1;
                return Press(p, TriggerEvent.Contact);
            }
            else if (p - _anchor >= s.ActivationDistance + s.ActivationPercent * 0.01 * _anchor)
            {
                return Press(p, TriggerEvent.Rearm);
            }

            return TriggerEvent.None;
        }

        private TriggerEvent UpdatePressed(double p, double previous, double elapsedMs, double sampleMs)
        {
            var s = _settings;
            HeldTime += elapsedMs;
            double multiplier = HoldMultiplier(HeldTime);

            // Fast path: one-sided CUSUM. Each new pressure sample adds its fall minus what a drag could fall since the
            // previous sample; rises and slow falls drain it back to zero. Repeated values are not new samples.
            bool fastDetector = s.FastFallSpeed > 0 || s.FastFallPercent > 0;
            bool fastFallBuilding = false;
            if (fastDetector)
            {
                if (p != previous)
                {
                    double allowance = s.FastFallSpeed + s.FastFallPercent * 0.01 * previous;
                    FallExcess = Math.Max(0, FallExcess + (previous - p) - allowance * sampleMs);
                }
                fastFallBuilding = FallExcess > 0;
            }

            // Slow path: the reference drifts towards the previous sample over the elapsed interval, so a sudden
            // drop is measured in full against the level held just before it. A fast fall freezes it.
            if (s.DriftTimeConstant > 0 && previous < _anchor && !fastFallBuilding)
                _anchor += (previous - _anchor) * Follow(elapsedMs, s.DriftTimeConstant);
            if (p > _anchor)
                _anchor = p;

            HoldReference = _anchor;
            CurrentReleaseDistance = ReleaseDistanceFor(_anchor, HeldTime);

            if (p <= s.LiftThreshold)
                return Release(p, TriggerEvent.Lift);
            if (fastDetector && FallExcess >= s.FastReleaseDistance * multiplier)
                return Release(p, TriggerEvent.FastRelease);
            if (_anchor - p >= CurrentReleaseDistance)
                return Release(p, TriggerEvent.Release);

            return TriggerEvent.None;
        }

        public double ReleaseDistanceFor(double reference, double heldTime)
        {
            var s = _settings;
            double distance = Math.Max(s.ReleaseDistance, s.ReleaseRatio * reference);
            distance = Math.Min(distance, Math.Max(s.MaxReleaseDistance, s.ReleaseDistance));
            return distance * HoldMultiplier(heldTime);
        }

        private double HoldMultiplier(double heldTime)
        {
            var s = _settings;
            if (s.HoldReleaseMultiplier == 1)
                return 1;
            double ramp = s.HoldTime > 0 ? Math.Min(1, heldTime / s.HoldTime) : 1;
            return 1 + (s.HoldReleaseMultiplier - 1) * ramp;
        }

        private TriggerEvent Press(double p, TriggerEvent reason)
        {
            Pressed = true;
            _fresh = false;
            _anchor = p;
            HeldTime = 0;
            FallExcess = 0;
            HoldReference = p;
            CurrentReleaseDistance = ReleaseDistanceFor(p, 0);
            return reason;
        }

        private TriggerEvent Release(double p, TriggerEvent reason)
        {
            Pressed = false;
            _fresh = p <= _settings.LiftThreshold;
            _anchor = p;
            return reason;
        }

        private static double Follow(double elapsedMs, double timeConstantMs)
            => 1 - Math.Exp(-elapsedMs / timeConstantMs);
    }
}
