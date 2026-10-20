using System;

namespace RapidTrigger
{
    /// <summary>
    /// Report timing from the PTK-670 custom firmware's pen-read counter instead of the host clock (opt-in).
    /// <para>
    /// That firmware (production ring500 0fc2521a, 2026-10-18, set to 1000 Hz with motion sync ON: locked to the USB
    /// frames) sends 1000 ID 0x1E reports/s and samples pen pressure every 2.000 ms. Byte 17 is a counter (u8, wraps)
    /// that steps by 1 on every pen read (2.000 ms) and repeats on the report in between. The host
    /// receives reports in bursts: on recordings, two consecutive pressure samples arrived anywhere from 0.2 to 4 ms
    /// apart, and a sample arriving 0.2 ms after the previous one gets a tenth of its drag allowance in the fast-fall
    /// detector. This clock gives the engine the tablet's own timing: each report gets the counter steps it carries
    /// × 2.000 ms (0 when the counter did not step). The engine adds repeated reports' time to the next pressure sample,
    /// so every sample gets an exact multiple of 2.000 ms: the counter difference since the previous sample's report.
    /// That holds even when the new pressure arrives one report after the counter step (~1.5% of samples on cdb17e54
    /// data); splitting each period evenly over the two reports gave those samples 1.5 periods and the next one half.
    /// Only 1000 Hz motion sync ON is handled. 999 Hz sync OFF samples every 2.004 ms (this clock then runs 0.2% slow,
    /// negligible).
    /// </para>
    /// <para>
    /// Analog pen (CP923, ring500 9f4a1b39, 2026-10-20; <c>analogPen</c>): a new pressure sample on every report, and the
    /// counter steps on every report: 1.000 ms per step. Every side-button change re-tunes the pen and sends no reports for
    /// 2-5 ms, across which the counter still steps only once. Gap rule: once host time runs more than 1.5 ms ahead of the
    /// counter's, the difference is added to that report. Reports that arrive early count against it (down to -1.5 ms), so
    /// bursts (a late report, then a few 0.1 ms apart) do not add time; at a steady 1 kHz the rule never fires.
    /// </para>
    /// <para>
    /// If the firmware changes and the counter no longer means this, or the toggle does not match the pen (a Pro Pen 3 with
    /// Analog Pen on runs at half speed, the CP923 with it off at double speed), its time drifts from the host clock: once
    /// the two differ by more than a factor of 1.25 over 2 s of reports, <see cref="Mismatch"/> is set and host time is
    /// used until the pen changes (tool bytes 20-24).
    /// Allocation-free, no OTD dependency.
    /// </para>
    /// </summary>
    public sealed class FirmwareSampleClock
    {
        public const byte ReportId = 0x1E;
        public const int StatusByte = 2;
        public const int CounterByte = 17;
        public const int ToolByte = 20;
        public const int ToolLength = 5;
        public const double DigitalSampleMs = 2.000;
        public const double AnalogSampleMs = 1.000;
        public const double GapSlackMs = 1.5;

        private const double CheckWindowMs = 2000;
        private const double MaxRatio = 1.25;

        private int _counter = -1;
        private double _checkHostMs;
        private double _checkFirmwareMs;
        private double _lagMs;
        private ulong _tool;

        public FirmwareSampleClock(bool analogPen = false)
        {
            AnalogPen = analogPen;
            SampleMs = analogPen ? AnalogSampleMs : DigitalSampleMs;
        }

        public bool AnalogPen { get; }

        /// <summary>Time per counter step: 2.000 ms (Pro Pen 3), 1.000 ms (analog pen).</summary>
        public double SampleMs { get; }

        /// <summary>The counter's time disagreed with the host clock; host time is used until the pen changes.</summary>
        public bool Mismatch { get; private set; }

        /// <summary>Analog pen: the last report's time includes host time the counter did not see (a report gap).</summary>
        public bool Gap { get; private set; }

        /// <summary>
        /// The pen's side buttons (status byte 2 bits 1-3) of a 0x1E report, -1 for any other report.
        /// </summary>
        public static int PenButtons(byte[]? raw)
            => raw != null && raw.Length > StatusByte && raw[0] == ReportId ? raw[StatusByte] & 0x0E : -1;

        /// <summary>
        /// Time since the previous report by the firmware counter. Returns false (use host time) for reports without the
        /// counter, for the first one, and after a host gap of <paramref name="resyncMs"/> or more, when the counter may
        /// have wrapped (every 256 steps, ~512 ms; ~256 ms with the analog pen).
        /// </summary>
        public bool TryElapsed(byte[]? raw, double hostElapsedMs, double resyncMs, out double elapsedMs)
        {
            elapsedMs = hostElapsedMs;
            Gap = false;
            if (raw == null || raw.Length <= CounterByte || raw[0] != ReportId)
            {
                _counter = -1;
                return false;
            }
            CheckPen(raw);
            if (Mismatch)
                return false;

            int last = _counter;
            _counter = raw[CounterByte];
            if (last < 0 || hostElapsedMs >= resyncMs)
            {
                _checkHostMs = 0;
                _checkFirmwareMs = 0;
                _lagMs = 0;
                return false;
            }

            double counterMs = (byte)(_counter - last) * SampleMs;
            elapsedMs = counterMs;
            if (AnalogPen)
            {
                _lagMs = Math.Max(_lagMs + hostElapsedMs - counterMs, -GapSlackMs);
                if (_lagMs > GapSlackMs)
                {
                    elapsedMs += _lagMs;
                    _lagMs = 0;
                    Gap = true;
                }
            }

            // On the counter's own time: the gap rule would otherwise follow the host clock for a pen this mode does not fit.
            _checkHostMs += hostElapsedMs;
            _checkFirmwareMs += counterMs;
            if (_checkHostMs >= CheckWindowMs)
            {
                double ratio = _checkFirmwareMs / _checkHostMs;
                Mismatch = ratio > MaxRatio || ratio < 1 / MaxRatio;
                _checkHostMs = 0;
                _checkFirmwareMs = 0;
                if (Mismatch)
                {
                    elapsedMs = hostElapsedMs;
                    Gap = false;
                    return false;
                }
            }
            return true;
        }

        // Another pen (tool bytes, zero outside proximity): its counter is judged afresh.
        private void CheckPen(byte[] raw)
        {
            if (raw.Length < ToolByte + ToolLength)
                return;
            ulong tool = 0;
            for (int i = 0; i < ToolLength; i++)
                tool = tool << 8 | raw[ToolByte + i];
            if (tool == 0 || tool == _tool)
                return;
            if (_tool != 0)
            {
                Mismatch = false;
                Reset();
            }
            _tool = tool;
        }

        /// <summary>Out of range: the counter is picked up again on the next report. The pen and <see cref="Mismatch"/> stay.</summary>
        public void Reset()
        {
            _counter = -1;
            _checkHostMs = 0;
            _checkFirmwareMs = 0;
            _lagMs = 0;
        }
    }
}
