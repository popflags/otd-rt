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

        /// <summary>
        /// A drop straight from DropoutPressure or more to the lift threshold is held back this long, in ms. If pressure
        /// comes back above the lift threshold in that time, the drop is ignored as a sensor dropout; otherwise it counts
        /// as a lift. The PTK-670 on 5 ms firmware reports 0 for 5-45 ms when pressed hard (from 7750 up, and in long
        /// 8191 / 0 bursts while saturated). Real lifts always pass through lower pressures first (the last sample before
        /// zero was at most ~3000 in every recording), so this only delays lifts that never happened. 0 = off.
        /// </summary>
        public double DropoutTime { get; set; } = 60;

        /// <summary>Lowest pressure a drop to zero can come from and still be treated as a possible dropout.</summary>
        public double DropoutPressure { get; set; } = 6000;

        /// <summary>At or below this the tip is always released, and the next press counts as a fresh contact.</summary>
        public double LiftThreshold { get; set; } = 2;

        /// <summary>
        /// Rise above the lowest pressure since the last release that presses again without lifting
        /// (fixed part; ActivationPercent adds a part that grows with that lowest pressure).
        /// </summary>
        public double ActivationDistance { get; set; } = 10;

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
        public double FastFallSpeed { get; set; } = 2;

        /// <summary>
        /// Fast-fall detector: the part of the drag allowance that grows with pressure, in % of the current pressure
        /// per ms. Hand wobble grows with force: on the PTK-670 recording, hold dips at 7000-8191 fell up to
        /// 13 raw/ms, while re-presses without lifting at lighter pressure need a smaller allowance to release.
        /// </summary>
        public double FastFallPercent { get; set; } = 0.19;

        /// <summary>
        /// Fast-fall detector: release once the fall in excess of the allowance adds up to this (a one-sided CUSUM).
        /// Smaller = earlier tap releases; must stay above what sensor noise can add up to.
        /// </summary>
        public double FastReleaseDistance { get; set; } = 10;

        /// <summary>
        /// Fast-fall detector: after a contact from the air, the drag allowance starts at this fraction and ramps linearly to
        /// the full allowance over HoldTime. Taps end before holds start dipping, so they release earlier, while holds and
        /// drags get the full allowance once they last. 1 = off. 0.4 over 250 ms: taps on the 5 ms recordings release
        /// ~1 ms earlier on average than 0.5 over 150 ms, no real hold cut on any recording. 0.15 is ~2.6 ms earlier but cuts
        /// a two-stage hold start on a 9 ms log (5104 -> 4977 at +60 ms) and tolerates much smaller dips early in a drag.
        /// </summary>
        public double TapAllowance { get; set; } = 0.4;

        /// <summary>
        /// Same ramp after a re-press without lifting. Kept milder than TapAllowance: a re-press inside a held motion
        /// continues as a hold (on the 9 ms logs, 0.15 here cut two holds again right after an intended re-press).
        /// </summary>
        public double RepressAllowance { get; set; } = 0.5;

        /// <summary>
        /// For this long after a press, only a lift releases: the fast and slow detectors keep tracking but cannot fire.
        /// Absorbs landing bounces right after contact (5065 -> 4970 -> 5207 within 6 ms on the 5 ms recording), which
        /// otherwise release and re-press as a double click. Releases of taps shorter than this wait until it ends. 0 = off.
        /// A release held back by it is dropped when the holdoff ends if pressure is no longer falling and has come back up
        /// from its low by the re-press distance (it would release and re-press right away).
        /// </summary>
        public double PressHoldoff { get; set; } = 8;

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

        /// <summary>
        /// Time over which TapAllowance / RepressAllowance ramp to the full allowance and the release distances to
        /// HoldReleaseMultiplier.
        /// </summary>
        public double HoldTime { get; set; } = 250;

        /// <summary>Release distance multiplier (both paths) reached after HoldTime. 1 = off.</summary>
        public double HoldReleaseMultiplier { get; set; } = 1;

        public TriggerSettings Clone() => (TriggerSettings)MemberwiseClone();
    }

    /// <summary>
    /// Rapid trigger state machine. Call <see cref="Update"/> once per tablet report with the raw pressure and the
    /// time since the previous report. Allocation-free.
    /// <para>
    /// The state machine runs on pressure <em>samples</em>, not reports. Tablets that report faster than they sample
    /// pressure repeat the last value in between (PTK-670 custom firmware: ~1 kHz reports, a new pressure value every
    /// 2nd report). A report whose pressure equals the last sample exactly only adds its time to the next sample, so a
    /// repeated stream gives the same events as the samples alone, and a tablet that samples every report is unaffected.
    /// </para>
    /// <para>
    /// While pressed, two detectors run side by side:
    /// a fast-fall detector (one-sided CUSUM on the per-sample fall, minus a drag speed allowance) that releases
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
        private bool _fresh = true;

        // Time of repeated reports since the last sample, handed to the state machine with the next sample.
        private double _pendingMs;

        // Time a suspected phantom contact has lasted; negative = none pending.
        private double _phantomMs = -1;

        // Time since a suspected dropout (max pressure straight to zero) started; negative = none pending.
        private double _dropoutMs = -1;

        // Report time held back while a dropout was pending, handed to the state machine once it resolves.
        private double _deferredMs;

        // Whether the current press started from a lifted pen (Contact) rather than a re-press.
        private bool _pressedFromContact;

        // A release that was due but held back by PressHoldoff (None if none).
        private TriggerEvent _heldOffRelease;

        // Highest pressure since the press, and the lowest since that peak: a held-back release is dropped if pressure has
        // come back up from that low by the re-press distance and the last sample did not fall.
        private double _pressPeak;
        private double _pressLow;
        private bool _lastSampleFell;

        public TriggerEngine(TriggerSettings settings)
        {
            _settings = settings.Clone();
        }

        public TriggerSettings Settings => _settings.Clone();

        public bool Pressed { get; private set; }

        /// <summary>Hold reference while pressed, trough while released.</summary>
        public double Anchor => _anchor;

        /// <summary>Slow-path release distance on the last pressed sample (kept after a release for diagnostics).</summary>
        public double CurrentReleaseDistance { get; private set; }

        /// <summary>Hold reference on the last pressed sample (kept after a release for diagnostics).</summary>
        public double HoldReference { get; private set; }

        /// <summary>Fast-fall detector state: accumulated fall in excess of the drag allowance.</summary>
        public double FallExcess { get; private set; }

        /// <summary>Time from the start of the current press to the last sample, in milliseconds.</summary>
        public double HeldTime { get; private set; }

        public void Reset()
        {
            Pressed = false;
            _anchor = 0;
            _previousPressure = 0;
            _pendingMs = 0;
            _fresh = true;
            _phantomMs = -1;
            _dropoutMs = -1;
            _deferredMs = 0;
            _heldOffRelease = TriggerEvent.None;
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

            // The same pressure as the last sample is the tablet repeating it, not a new sample: nothing changes, its
            // time goes to the next sample. The timers keep running on repeats: phantom confirmation is processed as
            // usual, a dropout never sees its zeros as repeats (the last sample stays at the level before the drop),
            // and a release held back by PressHoldoff fires as soon as the holdoff ends.
            if (p == _previousPressure && _phantomMs < 0 && _dropoutMs < 0)
            {
                _pendingMs += elapsedMs;
                if (_heldOffRelease != TriggerEvent.None && HeldTime + _pendingMs >= _settings.PressHoldoff)
                {
                    HeldTime += _pendingMs;
                    _pendingMs = 0;
                    return Rebounded(p) ? DropHeldOffRelease(p) : Release(p, _heldOffRelease);
                }
                return TriggerEvent.None;
            }

            elapsedMs += _pendingMs;
            _pendingMs = 0;

            if (HoldDropout(p, ref elapsedMs))
                return TriggerEvent.None;

            double previous = _previousPressure;
            _previousPressure = p;

            return Pressed ? UpdatePressed(p, previous, elapsedMs) : UpdateReleased(p, previous, elapsedMs);
        }

        /// <summary>
        /// Holds back a fall straight from DropoutPressure or more to zero until it has lasted DropoutTime. Returns true
        /// while it is held back. The previous pressure stays at the level before the drop, so if pressure comes back
        /// the zero never happened (no release while pressed; no fresh contact or lowered trough while released); once
        /// it resolves, the held-back time is added to <paramref name="elapsedMs"/>.
        /// </summary>
        private bool HoldDropout(double p, ref double elapsedMs)
        {
            var s = _settings;
            bool zero = p <= s.LiftThreshold;

            if (_dropoutMs < 0)
            {
                if (!zero || s.DropoutTime <= 0 || _previousPressure < Math.Max(s.DropoutPressure, s.LiftThreshold + 1))
                    return false;
                _dropoutMs = 0;
                _deferredMs = elapsedMs;
                return true;
            }

            if (zero && _dropoutMs + elapsedMs < s.DropoutTime)
            {
                _dropoutMs += elapsedMs;
                _deferredMs += elapsedMs;
                return true;
            }

            elapsedMs += _deferredMs;
            _dropoutMs = -1;
            _deferredMs = 0;
            return false;
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

        private TriggerEvent UpdatePressed(double p, double previous, double elapsedMs)
        {
            var s = _settings;
            HeldTime += elapsedMs;
            double multiplier = HoldMultiplier(HeldTime);

            // Fast path: one-sided CUSUM. Each pressure sample adds its fall minus what a drag could fall since the
            // previous sample; rises and slow falls drain it back to zero.
            bool fastDetector = s.FastFallSpeed > 0 || s.FastFallPercent > 0;
            bool fastFallBuilding = false;
            if (fastDetector)
            {
                double allowance = s.FastFallSpeed + s.FastFallPercent * 0.01 * previous;
                double start = _pressedFromContact ? s.TapAllowance : s.RepressAllowance;
                if (start != 1 && s.HoldTime > 0 && HeldTime < s.HoldTime)
                    allowance *= start + (1 - start) * HeldTime / s.HoldTime;
                FallExcess = Math.Max(0, FallExcess + (previous - p) - allowance * elapsedMs);
                fastFallBuilding = FallExcess > 0;
            }

            // Slow path: the reference drifts towards the previous sample over the elapsed interval, so a sudden
            // drop is measured in full against the level held just before it. A fast fall freezes it.
            if (s.DriftTimeConstant > 0 && previous < _anchor && !fastFallBuilding)
                _anchor += (previous - _anchor) * Follow(elapsedMs, s.DriftTimeConstant);
            if (p > _anchor)
                _anchor = p;
            if (p >= _pressPeak)
                _pressPeak = _pressLow = p;
            else if (p < _pressLow)
                _pressLow = p;
            _lastSampleFell = p < previous;

            HoldReference = _anchor;
            CurrentReleaseDistance = ReleaseDistanceFor(_anchor, HeldTime);

            if (p <= s.LiftThreshold)
                return Release(p, TriggerEvent.Lift);

            var due = fastDetector && FallExcess >= s.FastReleaseDistance * multiplier ? TriggerEvent.FastRelease
                : _anchor - p >= CurrentReleaseDistance ? TriggerEvent.Release
                : TriggerEvent.None;
            if (due != TriggerEvent.None && HeldTime < s.PressHoldoff)
            {
                _heldOffRelease = due;
                return TriggerEvent.None;
            }
            if (due != TriggerEvent.None && _heldOffRelease != TriggerEvent.None && Rebounded(p))
                return DropHeldOffRelease(p);
            _heldOffRelease = TriggerEvent.None;
            return due == TriggerEvent.None ? TriggerEvent.None : Release(p, due);
        }

        // Landing bounce (contact 3706 -> 3337 -> 3616 within 12 ms on the 2026-10-19 logs): the fall made a release due inside
        // the holdoff, but pressure is back up by the re-press distance and still rising, so releasing now would re-press
        // right away. A tap whose own fall has started by then (5562 -> 5083 -> 5413 -> 5377 -> 5269) still releases.
        private bool Rebounded(double p)
            => !_lastSampleFell && p > _pressLow
               && p - _pressLow >= _settings.ActivationDistance + _settings.ActivationPercent * 0.01 * _pressLow;

        // Stays pressed; both detectors start over from here.
        private TriggerEvent DropHeldOffRelease(double p)
        {
            _heldOffRelease = TriggerEvent.None;
            FallExcess = 0;
            _anchor = p;
            HoldReference = p;
            _pressPeak = _pressLow = p;
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
            _pressedFromContact = reason == TriggerEvent.Contact;
            _heldOffRelease = TriggerEvent.None;
            _fresh = false;
            _anchor = p;
            _pressPeak = _pressLow = p;
            HeldTime = 0;
            FallExcess = 0;
            HoldReference = p;
            CurrentReleaseDistance = ReleaseDistanceFor(p, 0);
            return reason;
        }

        private TriggerEvent Release(double p, TriggerEvent reason)
        {
            Pressed = false;
            _heldOffRelease = TriggerEvent.None;
            _fresh = p <= _settings.LiftThreshold;
            _anchor = p;
            return reason;
        }

        private static double Follow(double elapsedMs, double timeConstantMs)
            => 1 - Math.Exp(-elapsedMs / timeConstantMs);
    }
}
