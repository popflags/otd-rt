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

        /// <summary>
        /// A single sample that jumps at least this far from the previous one, up or down (while pressed, or released without
        /// a lift since; never at a contact), is held
        /// back like a dropout, for up to DropoutTime. If pressure comes back within this distance of the level before the
        /// jump in that time, the jump is ignored and nothing is counted across it; otherwise it is real and processed late.
        /// The PTK-670 on ring500 83a52e3c sends one- or two-sample glitches while pressed hard (8191 -> 692 -> 8191,
        /// 7424 -> 4496 -> 7380, 8117 -> 0 -> 8113, hover distance unchanged); real changes never exceeded ~1800 per 2 ms
        /// sample (falls) or ~520 (rises after the contact). 0 = off.
        /// </summary>
        public double GlitchJump { get; set; }

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

        /// <summary>
        /// Every press lasts at least this long, in ms: any release, a lift included, waits until then. Rapid releases make
        /// quick taps very short clicks (12 ms on the 2026-10-19 S4 League logs, the end of a 12 ms holdoff), and a game that
        /// reads the button once per frame misses a click that starts and ends between two frames (60 fps: 16.7 ms). Applies
        /// to every press: taps, brushes of a few ms and re-presses without lifting (owner: brushes are intended taps too, and
        /// an unintended one registering is fine). A held lift still fires when the time is up even if the pen is already back
        /// down, and the next sample is then a fresh contact, so a quick re-tap is not merged into the previous click. Keep it
        /// short (under ~50 ms): a held release needs reports to arrive to fire. 0 = off.
        /// </summary>
        public double MinimumClick { get; set; }

        /// <summary>
        /// Experimental, owner's request (2026-10-20): a touchdown click (a press from a lifted pen) goes out no sooner than this
        /// long after the previous touchdown click, in ms of host time; a touchdown that lands earlier clicks exactly then. Taps
        /// and drags alike (they cannot be told apart at touchdown). Re-presses without lifting are not delayed and do not count
        /// (landing double clicks 30-90 ms after a contact, re-presses near the end of a drag). Releases are not delayed; a touch
        /// that has already ended when its click goes out is clicked for as long as it lasted. At most one click waits: a touch
        /// that lands before it goes out joins it. The detector itself is unchanged (<see cref="TriggerEngine.Pressed"/>); the
        /// gated output is <see cref="TriggerEngine.OutputPressed"/> and what <see cref="TriggerEngine.Update(uint, double, double)"/>
        /// returns. The plugin's default is 500; 0 here so tests and tools measure the detector unless asked. 0 = off.
        /// </summary>
        public double TouchdownGate { get; set; }

        /// <summary>
        /// Fast-fall detector: allowance multiplier once a press has lasted HoldTime (drags and holds); the Tap / Re-press Allowance
        /// ramp before it is unchanged, so taps are not affected. For setups without re-presses (Activation Distance huge), where a
        /// release cannot be undone until the pen lifts: drag ends on the 2 ms gameplay logs dip at ~20-30 raw/ms and then hold level,
        /// while real lifts fall at 50-250 raw/ms. 4 removed 61 of 72 such drag-end cut-outs (the rest come from the slow path: raise
        /// Release Distance to 1000); drag ends then release ~20 ms later. With re-presses on (normal Activation Distance) it would
        /// swallow re-clicks inside drags (29.6-65 raw/ms falls). 1 = off.
        /// </summary>
        public double HoldAllowance { get; set; } = 1;

        /// <summary>
        /// Rise from the low after the peak that makes a release held back by Press Holdoff count as a landing bounce and be dropped,
        /// in raw units. 0 = the re-press distance (Activation Distance + Activation Percent), as before. Set it (e.g. 10) when
        /// Activation Distance is huge: that disables the bounce rule otherwise, and landing dips then release for good.
        /// </summary>
        public double ReboundDistance { get; set; }

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

        /// <summary>
        /// Analog pen (CP923 on the PTK-670 custom firmware, ring500 9f4a1b39, 2026-10-20): a new pressure sample on every report and
        /// no phantom max-pressure contacts, but every side-button change re-tunes the pen and steps the pressure: p10 / p50 / p90
        /// -160 / +12 / +225 raw (up to -318 / +409) on the report with the new button bits, then a ramp of 85-150 per report for ~5
        /// reports and an overshoot for ~4 more. Some button 1 presses fall ~110 per report for 3-6 reports before the new bits arrive;
        /// the re-tune starts with a 3-5 ms report gap, which the analog Firmware Sample Clock sees. On: the phantom check is skipped
        /// (a landing at max pressure presses at once), and pressure changes on a report flagged as a re-tune (button change or gap)
        /// and on the ButtonSettleReports reports after it are taken as the new level
        /// (<see cref="TriggerEngine.Update(uint, double, double, bool)"/>); a fresh contact ends that window. Off for the Pro Pen 3
        /// (it makes phantoms, and its buttons do not step the pressure).
        /// </summary>
        public bool AnalogPen { get; set; }

        /// <summary>
        /// Analog pen: for this many reports after a re-tune (side-button change, or the report gap that starts one), pressure changes
        /// are taken as the new level rather than as falls or rises (the flagged report always is). Counted in reports, not ms: the firmware's ramp is a number of
        /// pen reads, and the report gaps around a button change would use up a time window early. Lifts still release at once; a
        /// real release in the window is decided when it ends. 0 = only the report with the change.
        /// </summary>
        public double ButtonSettleReports { get; set; } = 10;

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
    /// <para>
    /// An optional output stage, the Touchdown Gate, can hold touchdown clicks back after the detector; the detector never sees
    /// it. <see cref="Pressed"/> is the detector's state, <see cref="OutputPressed"/> what the tablet should report.
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

        // Analog pen: reports since the last side-button change while its pressure step settles; negative = none.
        private int _settleReports = -1;

        // Time since a suspected dropout (max pressure straight to zero) started; negative = none pending.
        private double _dropoutMs = -1;

        // Report time held back while a dropout was pending, handed to the state machine once it resolves.
        private double _deferredMs;

        // Pressure before the pending dropout / glitch.
        private double _dropoutLevel;

        // Whether the current press started from a lifted pen (Contact) rather than a re-press.
        private bool _pressedFromContact;

        // A release that was due but held back by PressHoldoff (None if none).
        private TriggerEvent _heldOffRelease;

        // Highest pressure since the press, and the lowest since that peak: a held-back release is dropped if pressure has
        // come back up from that low by the re-press distance and the last sample did not fall.
        private double _pressPeak;
        private double _pressLow;
        private bool _lastSampleFell;

        // Touchdown Gate (output stage), on host time: when the last touchdown click went out (or will, while it waits).
        private double _hostMs;
        private double _lastTouchdownMs = double.NegativeInfinity;

        // A touchdown click waiting for the gate: when it goes out, when the engine last pressed for it, and the release the
        // engine made before it went out (None while the engine is pressed) with the click length it saw.
        private bool _clickWaiting;
        private double _clickDueMs;
        private double _clickPressedMs;
        private TriggerEvent _clickRelease;
        private double _clickLengthMs;

        // A waiting click that went out after its touch ended: it is released after its own length.
        private bool _replaying;
        private double _replayEndMs;
        private TriggerEvent _replayRelease;

        public TriggerEngine(TriggerSettings settings)
        {
            _settings = settings.Clone();
        }

        public TriggerSettings Settings => _settings.Clone();

        /// <summary>The detector's tip state (see <see cref="OutputPressed"/> for what to report).</summary>
        public bool Pressed { get; private set; }

        /// <summary>The tip state to report: <see cref="Pressed"/>, except while the Touchdown Gate holds a click back.</summary>
        public bool OutputPressed { get; private set; }

        /// <summary>What the detector decided on the last report (the output event can differ while the Touchdown Gate waits).</summary>
        public TriggerEvent DetectedEvent { get; private set; }

        /// <summary>
        /// Host time until a click held back by the Touchdown Gate goes out (or a replayed one ends), in ms; negative when
        /// nothing waits. The plugin arms a timer with it so the click goes out even when no report arrives.
        /// </summary>
        public double OutputDueInMs
            => _replaying ? Math.Max(0, _replayEndMs - _hostMs)
               : _clickWaiting ? Math.Max(0, _clickDueMs - _hostMs)
               : -1;

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
            _settleReports = -1;
            _dropoutMs = -1;
            _deferredMs = 0;
            _heldOffRelease = TriggerEvent.None;
            CurrentReleaseDistance = 0;
            HoldReference = 0;
            FallExcess = 0;
            HeldTime = 0;
            // The gate keeps its clock and the last touchdown; a click still waiting is dropped with the pen.
            OutputPressed = false;
            DetectedEvent = TriggerEvent.None;
            _clickWaiting = false;
            _replaying = false;
        }

        /// <summary>One report: raw pressure and the time since the previous report (also used as host time).</summary>
        public TriggerEvent Update(uint pressure, double elapsedMs) => Update(pressure, elapsedMs, elapsedMs);

        /// <summary>
        /// One report. <paramref name="elapsedMs"/> drives the detector (the plugin caps it at 50 ms and may take it from the
        /// tablet's sample counter); <paramref name="hostElapsedMs"/> is the uncapped PC time since the previous report or
        /// <see cref="Tick"/>, which the Touchdown Gate runs on. Returns the output event (the detected one when the gate is off).
        /// </summary>
        public TriggerEvent Update(uint pressure, double elapsedMs, double hostElapsedMs)
            => Update(pressure, elapsedMs, hostElapsedMs, false);

        /// <summary>
        /// One report, as above. <paramref name="retuned"/>: the analog pen re-tuned on this report (its side buttons differ from
        /// the previous report's, or reports were missing that the firmware counter did not see: the re-tune starts with such a gap,
        /// up to ~9 reports before the new button bits). Only used with <see cref="TriggerSettings.AnalogPen"/>: the pressure step
        /// that comes with it is not a fall or a rise.
        /// </summary>
        public TriggerEvent Update(uint pressure, double elapsedMs, double hostElapsedMs, bool retuned)
        {
            var detected = Detect(pressure, elapsedMs, retuned);
            DetectedEvent = detected;
            if (!(_settings.TouchdownGate > 0))
            {
                OutputPressed = Pressed;
                return detected;
            }
            if (hostElapsedMs > 0)
                _hostMs += hostElapsedMs;
            return Gate(detected);
        }

        /// <summary>
        /// Advances the Touchdown Gate's clock without a report (the plugin's timer, when no report arrives in time) and returns
        /// the output event if a held-back click goes out or ends now. The detector does not run.
        /// </summary>
        public TriggerEvent Tick(double hostElapsedMs)
        {
            DetectedEvent = TriggerEvent.None;
            if (!(_settings.TouchdownGate > 0))
                return TriggerEvent.None;
            if (hostElapsedMs > 0)
                _hostMs += hostElapsedMs;
            return Gate(TriggerEvent.None);
        }

        // Touchdown Gate. At most one output change per report, so a click held back and its release never share a report.
        private TriggerEvent Gate(TriggerEvent detected)
        {
            switch (detected)
            {
                case TriggerEvent.Contact:
                    if (_clickWaiting)
                    {
                        // Touched again before its click went out: one click, following this touch.
                        _clickRelease = TriggerEvent.None;
                        _clickPressedMs = _hostMs;
                        break;
                    }
                    double due = _lastTouchdownMs + _settings.TouchdownGate;
                    if (_hostMs >= due && !_replaying)
                    {
                        _lastTouchdownMs = _hostMs;
                        OutputPressed = true;
                        return TriggerEvent.Contact;
                    }
                    _clickWaiting = true;
                    _clickDueMs = Math.Max(due, _hostMs);
                    _clickPressedMs = _hostMs;
                    _clickRelease = TriggerEvent.None;
                    _lastTouchdownMs = _clickDueMs;
                    break;

                case TriggerEvent.Rearm:
                    if (_clickWaiting)
                    {
                        _clickRelease = TriggerEvent.None;
                        _clickPressedMs = _hostMs;
                    }
                    else if (_replaying)
                    {
                        // Pressed again while the replayed click is down: it stays down and follows the engine from here.
                        _replaying = false;
                    }
                    else if (!OutputPressed)
                    {
                        OutputPressed = true;
                        return TriggerEvent.Rearm;
                    }
                    break;

                case TriggerEvent.Release:
                case TriggerEvent.Lift:
                case TriggerEvent.FastRelease:
                    if (_clickWaiting)
                    {
                        _clickRelease = detected;
                        _clickLengthMs = _hostMs - _clickPressedMs;
                    }
                    else if (OutputPressed && !_replaying)
                    {
                        OutputPressed = false;
                        return detected;
                    }
                    break;
            }

            if (_replaying && _hostMs >= _replayEndMs)
            {
                _replaying = false;
                OutputPressed = false;
                return _replayRelease;
            }
            if (_clickWaiting && !_replaying && _hostMs >= _clickDueMs)
            {
                _clickWaiting = false;
                OutputPressed = true;
                if (_clickRelease != TriggerEvent.None)
                {
                    _replaying = true;
                    _replayEndMs = _hostMs + _clickLengthMs;
                    _replayRelease = _clickRelease;
                }
                return TriggerEvent.Contact;
            }
            return TriggerEvent.None;
        }

        private TriggerEvent Detect(uint pressure, double elapsedMs, bool retuned)
        {
            if (!(elapsedMs > 0))
                elapsedMs = 0;

            double p = pressure;

            bool settling = false;
            if (_settings.AnalogPen)
            {
                if (retuned)
                    _settleReports = 0;
                else if (_settleReports >= 0 && ++_settleReports > _settings.ButtonSettleReports)
                    _settleReports = -1;
                settling = _settleReports >= 0;
            }

            // The same pressure as the last sample is the tablet repeating it, not a new sample: nothing changes, its
            // time goes to the next sample. The timers keep running on repeats: phantom confirmation is processed as
            // usual, a dropout never sees its zeros as repeats (the last sample stays at the level before the drop),
            // and a release held back by PressHoldoff fires as soon as the holdoff ends.
            if (p == _previousPressure && _phantomMs < 0 && _dropoutMs < 0)
            {
                _pendingMs += elapsedMs;
                if (_heldOffRelease != TriggerEvent.None && HeldTime + _pendingMs >= HoldUntil(_heldOffRelease))
                {
                    HeldTime += _pendingMs;
                    _pendingMs = 0;
                    return FireHeldOffRelease(p);
                }
                return TriggerEvent.None;
            }

            elapsedMs += _pendingMs;
            _pendingMs = 0;

            // Analog pen, side button changed: the pen re-tunes and its pressure steps by up to ~400 within a few reports.
            // Lifts and fresh contacts are judged as usual.
            if (settling && _phantomMs < 0 && _dropoutMs < 0 && p > _settings.LiftThreshold && (Pressed || !_fresh))
                return Rebase(p, elapsedMs);

            if (HoldDropout(p, ref elapsedMs, out bool glitchIgnored))
                return TriggerEvent.None;
            if (glitchIgnored)
                _previousPressure = p;

            double previous = _previousPressure;
            _previousPressure = p;

            return Pressed ? UpdatePressed(p, previous, elapsedMs) : UpdateReleased(p, previous, elapsedMs);
        }

        /// <summary>
        /// Takes <paramref name="p"/> as the new level: every reference (hold reference or trough, the press's peak and low) moves
        /// by the step, so it counts as neither a fall nor a rise, and the fast-fall excess stays as it was. Time still passes, so
        /// a release held back by PressHoldoff / MinimumClick fires when it is due, like on a repeated report.
        /// </summary>
        private TriggerEvent Rebase(double p, double elapsedMs)
        {
            double step = p - _previousPressure;
            _previousPressure = p;
            if (!Pressed)
            {
                // The window also takes in the hand's own motion; falls inside it and rises outside would otherwise walk the
                // trough below zero (button tests on 1792513436: -8132 in 5 s, then a re-press at Activation Distance 10999).
                _anchor = Math.Max(0, _anchor + step);
                return TriggerEvent.None;
            }

            _anchor += step;

            _pressPeak += step;
            _pressLow += step;
            HeldTime += elapsedMs;
            HoldReference = _anchor;
            CurrentReleaseDistance = ReleaseDistanceFor(_anchor, HeldTime);
            return _heldOffRelease != TriggerEvent.None && HeldTime >= HoldUntil(_heldOffRelease)
                ? FireHeldOffRelease(p)
                : TriggerEvent.None;
        }

        /// <summary>
        /// Holds back a fall straight from DropoutPressure or more to zero, or a one-sample jump of GlitchJump or more, until
        /// it has lasted DropoutTime. Returns true while it is held back. The previous pressure stays at the level before
        /// it, so if pressure comes back the zero never happened (no release while pressed; no fresh contact or lowered
        /// trough while released). A glitch that comes back within GlitchJump of that level also counts no change across
        /// it (<paramref name="glitchIgnored"/>). Once it resolves, the held-back time is added to <paramref name="elapsedMs"/>.
        /// </summary>
        private bool HoldDropout(double p, ref double elapsedMs, out bool glitchIgnored)
        {
            var s = _settings;
            glitchIgnored = false;
            bool zero = p <= s.LiftThreshold;
            bool jumps = s.GlitchJump > 0;

            if (_dropoutMs < 0)
            {
                bool dropout = zero && _previousPressure >= Math.Max(s.DropoutPressure, s.LiftThreshold + 1);
                // Not before a fresh contact has pressed: the phantom guard handles a contact that starts at 8191.
                bool glitch = jumps && (Pressed || !_fresh) && _previousPressure > s.LiftThreshold
                    && Math.Abs(p - _previousPressure) >= s.GlitchJump;
                // Not while a phantom contact (a lone 8191 after a lift) waits for confirmation: a zero there means it is gone,
                // and holding it delayed the real contact right after it (2026-10-20 diagnostics: 8191 8191 0 550 pressed on 1938).
                if (s.DropoutTime <= 0 || _phantomMs >= 0 || !(dropout || glitch))
                    return false;
                _dropoutMs = 0;
                _dropoutLevel = _previousPressure;
                _deferredMs = elapsedMs;
                return true;
            }

            bool away = zero || (jumps && Math.Abs(p - _dropoutLevel) >= s.GlitchJump);
            if (away && _dropoutMs + elapsedMs < s.DropoutTime)
            {
                _dropoutMs += elapsedMs;
                _deferredMs += elapsedMs;
                return true;
            }

            glitchIgnored = !away && jumps;
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
                // The analog pen makes no phantoms (0 in ~230 tip-ons).
                if (!s.AnalogPen && s.PhantomContactPressure > 0 && p >= s.PhantomContactPressure)
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
                else if (s.HoldAllowance != 1 && HeldTime >= s.HoldTime)
                    allowance *= s.HoldAllowance;
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

            // A lift held back by MinimumClick is final, even if the pen is back down before it fires.
            if (_heldOffRelease == TriggerEvent.Lift)
                return HeldTime < s.MinimumClick ? TriggerEvent.None : FireHeldOffRelease(p);
            if (p <= s.LiftThreshold)
            {
                if (HeldTime < s.MinimumClick)
                {
                    _heldOffRelease = TriggerEvent.Lift;
                    return TriggerEvent.None;
                }
                return Release(p, TriggerEvent.Lift);
            }

            var due = fastDetector && FallExcess >= s.FastReleaseDistance * multiplier ? TriggerEvent.FastRelease
                : _anchor - p >= CurrentReleaseDistance ? TriggerEvent.Release
                : TriggerEvent.None;
            if (due != TriggerEvent.None && HeldTime < HoldUntil(due))
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
               && p - _pressLow >= (_settings.ReboundDistance > 0 ? _settings.ReboundDistance
                   : _settings.ActivationDistance + _settings.ActivationPercent * 0.01 * _pressLow);

        // How long after the press a release of this kind may fire: lifts wait for MinimumClick, other releases also for
        // PressHoldoff.
        private double HoldUntil(TriggerEvent release)
            => release == TriggerEvent.Lift ? _settings.MinimumClick : Math.Max(_settings.PressHoldoff, _settings.MinimumClick);

        // Fires a release held back by PressHoldoff / MinimumClick. A held lift always fires, and the next sample is a fresh
        // contact even if the pen is already back down; other releases are dropped on a landing bounce (Rebounded).
        private TriggerEvent FireHeldOffRelease(double p)
        {
            if (_heldOffRelease == TriggerEvent.Lift)
            {
                Release(p, TriggerEvent.Lift);
                _fresh = true;
                return TriggerEvent.Lift;
            }
            return Rebounded(p) ? DropHeldOffRelease(p) : Release(p, _heldOffRelease);
        }

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
            // A re-tune in the air (button change or range-entry gap while hovering) does not freeze the landing that follows.
            if (_pressedFromContact)
                _settleReports = -1;
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
