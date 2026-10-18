using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RapidTrigger.Tests
{
    public class FirmwareSampleClockTests
    {
        private const double Resync = 50;
        private const double P = FirmwareSampleClock.SampleMs;

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
        public void EachReportGetsTheCounterStepsItCarriesWhateverTheHostTiming()
        {
            // Sample, repeat, sample, repeat... delivered in bursts.
            var clock = new FirmwareSampleClock();
            Assert.False(clock.TryElapsed(Report(7), 0, Resync, out _));

            Assert.Equal(0, Elapsed(clock, 7, host: 0.1), 6);
            Assert.Equal(P, Elapsed(clock, 8, host: 3.8), 6);
            Assert.Equal(0, Elapsed(clock, 8, host: 0.1), 6);
            Assert.Equal(P, Elapsed(clock, 9, host: 0.1), 6);
        }

        [Fact]
        public void SkippedSamplesCountInFull()
        {
            var clock = new FirmwareSampleClock();
            clock.TryElapsed(Report(10), 0, Resync, out _);

            Assert.Equal(3 * P, Elapsed(clock, 13), 6);
            Assert.Equal(0, Elapsed(clock, 13), 6);
            Assert.Equal(0, Elapsed(clock, 13), 6);
            Assert.Equal(2 * P, Elapsed(clock, 15), 6);
        }

        // Real PTK-670 data, ~1.5% of samples: the new pressure arrives one report after the counter step.
        // Per report (S = counter stepped, p = pressure changed): [S p][. _][S _][. p][. _][S p].
        // Counters are relative to the last report before the pattern.
        private static readonly (int Steps, uint Pressure)[] OutOfPhase =
        {
            (1, 5000), (0, 5000), (1, 5000), (0, 4975), (0, 4975), (1, 4950),
        };

        // A hold at ~5000 for 300 ms, one new sample (5000 / 5001 alternating) per counter step, every 2nd report.
        private static IEnumerable<(int Steps, uint Pressure)> Hold()
            => Enumerable.Range(0, 300).Select(i => (i % 2 == 0 ? 1 : 0, (uint)(5000 + i / 2 % 2)));

        /// <summary>Runs reports through the clock; returns each report's elapsed time.</summary>
        private static List<double> ClockTimes(IEnumerable<(int Steps, uint Pressure)> reports)
        {
            var clock = new FirmwareSampleClock();
            int counter = 0;
            clock.TryElapsed(Report(counter), 0, Resync, out _);
            return reports.Select(r => Elapsed(clock, counter += r.Steps)).ToList();
        }

        [Fact]
        public void SampleArrivingOneReportAfterTheCounterStepStillGetsExactly2004Ms()
        {
            // Sum the time per pressure sample the way the engine does: a report with the last sample's pressure adds
            // its time to the next sample. The pattern's first report brings 5000 after a 5001 sample.
            var reports = new[] { (0, 5001u) }.Concat(OutOfPhase).ToArray();
            var times = ClockTimes(reports);
            var sampleTimes = new List<double>();
            double pending = 0;
            for (int i = 1; i < reports.Length; i++)
            {
                pending += times[i];
                if (reports[i].Item2 != reports[i - 1].Item2)
                {
                    sampleTimes.Add(Math.Round(pending, 6));
                    pending = 0;
                }
            }

            // The [S p] sample, the late [. p] one and the next [S p] one: one period each. Splitting each period evenly
            // over the two reports gave the late one 1.5 periods and the next half a period: half its drag allowance.
            Assert.Equal(new[] { P, P, P }, sampleTimes);
        }

        [Fact]
        public void SampleAfterALateSampleIsNotJudgedOnHalfAPeriod()
        {
            // The hold, then the pattern's two falls of 25. The drag allowance at 5000 is 2 + 0.19% = 11.5 units/ms:
            // 23 per 2 ms sample, so neither fall adds up to Fast Release Distance (10).
            var reports = Hold().Concat(OutOfPhase).ToList();
            var times = ClockTimes(reports);

            var engine = new TriggerEngine(new TriggerSettings());
            engine.Update(0, 1);
            var events = reports.Select((r, i) => engine.Update(r.Pressure, times[i])).Where(e => e != TriggerEvent.None);
            Assert.Equal(new[] { TriggerEvent.Contact }, events);

            // The even split's timing for the pattern (half a period per report; the late sample 1.5 periods in total, the
            // next one half a period) releases on the second fall: 25 - 11.5 = 13.5.
            var even = new TriggerEngine(new TriggerSettings());
            even.Update(0, 1);
            foreach (var r in Hold())
                even.Update(r.Pressure, P / 2);
            Assert.Equal(TriggerEvent.None, even.Update(5000, P / 2));
            Assert.Equal(TriggerEvent.None, even.Update(4975, 1.5 * P));
            Assert.Equal(TriggerEvent.FastRelease, even.Update(4950, P / 2));
        }

        [Fact]
        public void CounterWraps()
        {
            var clock = new FirmwareSampleClock();
            clock.TryElapsed(Report(255), 0, Resync, out _);

            Assert.Equal(P, Elapsed(clock, 0), 6);
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
            Assert.Equal(P, Elapsed(clock, 201), 6);
        }

        [Fact]
        public void CounterThatDoesNotMatchTheHostClockFallsBackToHostTime()
        {
            // The counter stepping on every report (an analog pen on 0fc2521a, 1.004 ms per step): 2 ms per 1 ms report.
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
