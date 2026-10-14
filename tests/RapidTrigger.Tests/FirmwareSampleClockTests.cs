using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RapidTrigger.Tests
{
    public class FirmwareSampleClockTests
    {
        private const double Resync = 50;

        private static byte[] Report(int counter, byte id = FirmwareSampleClock.ReportId)
        {
            var raw = new byte[20];
            raw[0] = id;
            raw[FirmwareSampleClock.CounterByte] = (byte)counter;
            return raw;
        }

        private static double Elapsed(FirmwareSampleClock clock, int counter, double host = 1)
        {
            Assert.True(clock.TryElapsed(Report(counter), host, Resync, out double elapsed));
            return elapsed;
        }

        [Fact]
        public void EveryReportGetsHalfASamplePeriodWhateverTheHostTiming()
        {
            // Sample, repeat, sample, repeat... delivered in bursts.
            var clock = new FirmwareSampleClock();
            Assert.False(clock.TryElapsed(Report(7), 0, Resync, out _));

            Assert.Equal(1.002, Elapsed(clock, 7, host: 0.1), 6);
            Assert.Equal(1.002, Elapsed(clock, 8, host: 3.8), 6);
            Assert.Equal(1.002, Elapsed(clock, 8, host: 0.1), 6);
            Assert.Equal(1.002, Elapsed(clock, 9, host: 0.1), 6);
        }

        [Fact]
        public void SkippedSamplesCountInFull()
        {
            var clock = new FirmwareSampleClock();
            clock.TryElapsed(Report(10), 0, Resync, out _);

            Assert.Equal(3 * 2.004, Elapsed(clock, 13), 6);
            Assert.Equal(1.002, Elapsed(clock, 13), 6);
            Assert.Equal(0, Elapsed(clock, 13), 6);
            Assert.Equal(2 * 2.004 - 1.002, Elapsed(clock, 15), 6);
        }

        [Fact]
        public void CounterWraps()
        {
            var clock = new FirmwareSampleClock();
            clock.TryElapsed(Report(255), 0, Resync, out _);

            Assert.Equal(2.004, Elapsed(clock, 0), 6);
        }

        [Fact]
        public void OtherReportsAndGapsUseHostTime()
        {
            var clock = new FirmwareSampleClock();
            clock.TryElapsed(Report(1), 0, Resync, out _);

            Assert.False(clock.TryElapsed(Report(2, id: 0x1F), 1.5, Resync, out double elapsed));
            Assert.Equal(1.5, elapsed);
            Assert.False(clock.TryElapsed(new byte[] { 0x1E, 0, 0 }, 1.5, Resync, out _));

            // Counter known again after one report, then a gap long enough for it to wrap: resynchronize.
            clock.TryElapsed(Report(3), 1, Resync, out _);
            Assert.False(clock.TryElapsed(Report(200), 50, Resync, out elapsed));
            Assert.Equal(50, elapsed);
            Assert.Equal(2.004, Elapsed(clock, 201), 6);
        }

        [Fact]
        public void CounterThatDoesNotMatchTheHostClockFallsBackToHostTime()
        {
            // A firmware where the counter steps on every report: 2.004 ms per 1 ms report.
            var clock = new FirmwareSampleClock();
            clock.TryElapsed(Report(0), 0, Resync, out _);
            int counter = 0;
            int firmwareReports = 0;
            while (clock.TryElapsed(Report(++counter), 1, Resync, out _))
                firmwareReports++;

            Assert.True(clock.Mismatch);
            Assert.InRange(firmwareReports, 1900, 2000);
            Assert.False(clock.TryElapsed(Report(++counter), 1, Resync, out double elapsed));
            Assert.Equal(1, elapsed);
        }

        [Fact]
        public void DragDeliveredInBurstsHoldsWithTheFirmwareClock()
        {
            // The drag from DragWithPressureEvery2ndReportDoesNotCutOut, pressure every 2nd report, but delivered the way
            // the 2026-10-14 recordings arrived: consecutive samples alternately 3.8 ms and 0.2 ms apart by the host clock.
            var signal = Signals.SampleAndHold(Signals.Ramp(0, 5000, 60).Concat(Signals.Drag(5000, 8000, 0.10, 400, 0.015, 5, seed: 4)), 2)
                .ToArray();
            double[] hostPattern = { 3.708, 0.1, 0.1, 0.1 };

            List<TriggerEvent> Run(bool firmwareClock)
            {
                var engine = new TriggerEngine(new TriggerSettings());
                var clock = new FirmwareSampleClock();
                var events = new List<TriggerEvent>();
                for (int i = 0; i < signal.Length; i++)
                {
                    double elapsed = hostPattern[i % 4];
                    if (firmwareClock && clock.TryElapsed(Report(i / 2), elapsed, Resync, out double fw))
                        elapsed = fw;
                    var ev = engine.Update((uint)System.Math.Round(signal[i]), elapsed);
                    if (ev != TriggerEvent.None)
                        events.Add(ev);
                }
                return events;
            }

            Assert.Contains(TriggerEvent.FastRelease, Run(firmwareClock: false));
            Assert.Equal(new[] { TriggerEvent.Contact }, Run(firmwareClock: true));
        }
    }
}
