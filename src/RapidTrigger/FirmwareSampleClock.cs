namespace RapidTrigger
{
    /// <summary>
    /// Report timing from the PTK-670 custom firmware's pen-read counter instead of the host clock (opt-in).
    /// <para>
    /// That firmware sends ~1000 reports/s as ID 0x1E reports and samples pen pressure every 2.004 ms. Byte 17 is a
    /// counter (u8, wraps) that steps by 1 on every report carrying a new pressure sample and repeats on the report in
    /// between. The host receives reports in bursts: on recordings, two consecutive pressure samples arrived anywhere
    /// from 0.2 to 4 ms apart, and a sample arriving 0.2 ms after the previous one gets a tenth of its drag allowance in
    /// the fast-fall detector. This clock gives the engine the tablet's own timing: every report half a sample period,
    /// a report that skipped samples the time of the samples it skipped. Allocation-free, no OTD dependency.
    /// </para>
    /// <para>
    /// If the firmware changes and the counter no longer means this, its time drifts from the host clock: once the two
    /// differ by more than a factor of 1.25 over 2 s of reports, <see cref="Mismatch"/> is set and host time is used
    /// from then on.
    /// </para>
    /// </summary>
    public sealed class FirmwareSampleClock
    {
        public const byte ReportId = 0x1E;
        public const int CounterByte = 17;
        public const double SampleMs = 2.004;

        private const double CheckWindowMs = 2000;
        private const double MaxRatio = 1.25;

        private int _counter = -1;
        private double _checkHostMs;
        private double _checkFirmwareMs;

        // The report after the last sample already got its half period.
        private bool _halfGiven;

        /// <summary>The counter's time disagreed with the host clock; host time is used from then on.</summary>
        public bool Mismatch { get; private set; }

        /// <summary>
        /// Time since the previous report by the firmware counter. Returns false (use host time) for reports without the
        /// counter, for the first one, and after a host gap of <paramref name="resyncMs"/> or more, when the counter may
        /// have wrapped (every 256 samples, ~513 ms).
        /// </summary>
        public bool TryElapsed(byte[]? raw, double hostElapsedMs, double resyncMs, out double elapsedMs)
        {
            elapsedMs = hostElapsedMs;
            if (Mismatch)
                return false;
            if (raw == null || raw.Length <= CounterByte || raw[0] != ReportId)
            {
                _counter = -1;
                return false;
            }

            int last = _counter;
            _counter = raw[CounterByte];
            if (last < 0 || hostElapsedMs >= resyncMs)
            {
                _halfGiven = false;
                _checkHostMs = 0;
                _checkFirmwareMs = 0;
                return false;
            }

            int steps = (byte)(_counter - last);
            if (steps == 0)
            {
                elapsedMs = _halfGiven ? 0 : SampleMs / 2;
                _halfGiven = true;
            }
            else
            {
                elapsedMs = steps * SampleMs - (_halfGiven ? SampleMs / 2 : 0);
                _halfGiven = false;
            }

            _checkHostMs += hostElapsedMs;
            _checkFirmwareMs += elapsedMs;
            if (_checkHostMs >= CheckWindowMs)
            {
                double ratio = _checkFirmwareMs / _checkHostMs;
                Mismatch = ratio > MaxRatio || ratio < 1 / MaxRatio;
                _checkHostMs = 0;
                _checkFirmwareMs = 0;
                if (Mismatch)
                {
                    elapsedMs = hostElapsedMs;
                    return false;
                }
            }
            return true;
        }

        public void Reset()
        {
            _counter = -1;
            _halfGiven = false;
            _checkHostMs = 0;
            _checkFirmwareMs = 0;
        }
    }
}
