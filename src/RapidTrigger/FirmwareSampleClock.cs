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
    /// negligible); an analog pen steps the counter on every report (1.004 ms), which the host-clock check below catches
    /// (time runs 2x fast) and falls back to host time.
    /// Allocation-free, no OTD dependency.
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
        public const double SampleMs = 2.000;

        private const double CheckWindowMs = 2000;
        private const double MaxRatio = 1.25;

        private int _counter = -1;
        private double _checkHostMs;
        private double _checkFirmwareMs;

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
                _checkHostMs = 0;
                _checkFirmwareMs = 0;
                return false;
            }

            elapsedMs = (byte)(_counter - last) * SampleMs;

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
            _checkHostMs = 0;
            _checkFirmwareMs = 0;
        }
    }
}
