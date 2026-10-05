using System;
using System.Linq;
using Xunit;

namespace RapidTrigger.Tests
{
    public class TriggerEngineTests
    {
        private static TriggerEngine Engine(TriggerSettings? settings = null) => new(settings ?? new TriggerSettings());

        // Peak-distance rapid trigger without drift or fast detector, like the previous plugin.
        private static TriggerSettings Classic(double releaseDistance = 250) => new()
        {
            ReleaseDistance = releaseDistance,
            DriftTimeConstant = 0,
            FastFallSpeed = 0,
            FastFallPercent = 0,
        };

        private static bool IsRelease(TriggerEvent e) => e is TriggerEvent.Release or TriggerEvent.FastRelease or TriggerEvent.Lift;

        private static void HoldAt(TriggerEngine engine, double level)
        {
            Signals.Run(engine, Signals.Ramp(0, level, 40));
            Signals.Run(engine, Signals.Hold(level, 100));
        }

        [Fact]
        public void FirstContactPressesOnTheFirstReportAboveThreshold()
        {
            var engine = Engine();
            Assert.Equal(TriggerEvent.None, engine.Update(0, 1));
            Assert.Equal(TriggerEvent.None, engine.Update(3, 1));
            Assert.Equal(TriggerEvent.Contact, engine.Update(4, 1));
            Assert.True(engine.Pressed);
        }

        [Fact]
        public void FirstContactIsNotDelayedByActivationDistance()
        {
            var engine = Engine(new TriggerSettings { ActivationDistance = 500 });
            engine.Update(0, 1);
            Assert.Equal(TriggerEvent.Contact, engine.Update(12, 1));
        }

        [Fact]
        public void PhantomMaxPressureSampleAfterALiftDoesNotPress()
        {
            // From the recordings: one sample at exactly 8191, ~40 ms after a lift, then 0.
            var engine = Engine();
            var events = Signals.Run(engine, Signals.Samples(9, 0, 0, 0, 0, 8191, 0, 0));

            Assert.Empty(events);
            Assert.False(engine.Pressed);
        }

        [Fact]
        public void ContactStartingAtMaxPressesOnceItMovesOrLasts()
        {
            var moved = Engine();
            var events = Signals.Run(moved, Signals.Samples(9, 0, 8191, 7900));
            Assert.Equal((18, TriggerEvent.Contact), events.Single());

            var held = Engine();
            events = Signals.Run(held, Signals.Samples(9, 0, 8191, 8191, 8191));
            Assert.Equal((9 + 20, TriggerEvent.Contact), events.Single());
        }

        [Fact]
        public void ContactBelowPhantomPressureIsNotDelayed()
        {
            var engine = Engine();
            engine.Update(0, 1);
            Assert.Equal(TriggerEvent.Contact, engine.Update(8190, 1));
        }

        [Fact]
        public void LiftAlwaysReleases()
        {
            var engine = Engine(new TriggerSettings { ReleaseDistance = 100000, FastFallSpeed = 0, FastFallPercent = 0 });
            engine.Update(3000, 1);
            Assert.Equal(TriggerEvent.Lift, engine.Update(2, 1));
            Assert.False(engine.Pressed);
        }

        [Fact]
        public void FastFallReleasesWithinTwoReports()
        {
            // 50 units/ms from a 4000 hold: 30 units/ms above the drag allowance, 40 needed.
            var engine = Engine();
            HoldAt(engine, 4000);

            var events = Signals.Run(engine, Signals.Ramp(4000, 0, 80));

            Assert.Equal(TriggerEvent.FastRelease, events[0].Event);
            Assert.InRange(events[0].Index, 0, 1);
        }

        [Fact]
        public void FastDetectorBeatsClassicRapidTriggerOnARoundedRelease()
        {
            // Real releases start slowly at the peak: half a cosine from 4500 down to 1500 over 35 ms.
            var detector = Engine();
            var classic = Engine(Classic());
            HoldAt(detector, 4500);
            HoldAt(classic, 4500);
            var fall = Enumerable.Range(1, 35).Select(i => 3000 + 1500 * Math.Cos(Math.PI * i / 35)).ToArray();

            int detectorIndex = Signals.Run(detector, fall).First().Index;
            int classicIndex = Signals.Run(classic, fall).First().Index;

            Assert.True(detectorIndex < classicIndex, $"detector {detectorIndex} vs classic {classicIndex}");
        }

        [Fact]
        public void SlowFallBelowDragSpeedNeverTriggersTheFastDetector()
        {
            // 9 units/ms for 30 ms: 270 units down, never faster than the allowance (0.5 + 0.25% of ~3800 = 10 units/ms).
            var engine = Engine(new TriggerSettings { ReleaseDistance = 100000 });
            HoldAt(engine, 4000);

            var events = Signals.Run(engine, Signals.Ramp(4000, 3730, 30));

            Assert.Empty(events);
        }

        // Defaults are tuned for pressure sampled every 5 ms (PTK-670, current firmware; ~9 ms before). With a new noisy sample every ms, noise adds
        // up in the fast detector: these are the settings for that case (fixed 20 units/ms allowance, distance 40).
        private static TriggerSettings EveryReportSampled() => new() { FastFallSpeed = 20, FastFallPercent = 0, FastReleaseDistance = 40 };

        [Theory]
        // +-25 uniform noise (sigma ~14) on a 3000 hold for 20 seconds.
        [InlineData(9, 25)]
        [InlineData(1, 25)]
        // 5 ms sampling: +-15 (sigma ~8.7, about twice the 3-5 measured on steady holds in the 5 ms recording). At +-25 the
        // 2.5.1 allowance at 3000 (7.7 units/ms, 38 per sample) is crossed now and then; 2.5.0's (8.1) just held.
        [InlineData(5, 15)]
        public void SensorNoiseOnAStillHoldDoesNotRelease(int sampleReports, double noise)
        {
            var engine = Engine(sampleReports == 1 ? EveryReportSampled() : null);
            HoldAt(engine, 3000);

            var events = Signals.Run(engine, Signals.SampleAndHold(Signals.Drag(3000, 20000, 0, 400, 0, noise, seed: 7), sampleReports));

            Assert.Empty(events);
        }

        [Theory]
        // Defaults, PTK-670 sampling: 15% dips over 400 ms + 2% tremor peak at ~11.6 units/ms, right at the allowance at
        // 5000 (2 + 0.19% = 11.5 units/ms; the peaks rarely line up within a sample, so it holds). Real hold dips on the recording peaked at 13 units/ms at 7000-8191.
        [InlineData(9, 0.15, 0.02)]
        // 5 ms sampling (current firmware): the same +-15 noise per sample is larger relative to the allowance per
        // sample, so the 15% dip drag above sits at the edge: over many seeds the 0.5/0.25 and 1.5/0.22 allowances cut
        // out about equally often, 2/0.19 (2.5.1) a bit more often. 10% dips hold.
        [InlineData(5, 0.10, 0.02)]
        // The harsher made-up drag (30% dips + 3% tremor, ~20 units/ms) only holds with the fixed 20 units/ms
        // allowance. That is the trade made for faster releases and re-presses without lifting.
        [InlineData(1, 0.30, 0.03)]
        public void SlowDipsTremorAndNoiseDuringADragDoNotRelease(int sampleReports, double dip, double tremor)
        {
            var engine = Engine(sampleReports == 1 ? EveryReportSampled() : null);
            Signals.Run(engine, Signals.Ramp(0, 5000, 60));

            var events = Signals.Run(engine, Signals.SampleAndHold(Signals.Drag(5000, 5000, dip, 400, tremor, 15, seed: 1), sampleReports));

            Assert.Empty(events);
            Assert.True(engine.Pressed);
        }

        [Fact]
        public void HarshSyntheticDragCutsOutWithDefaults()
        {
            // Documents the trade: 30% dips over 400 ms + 3% tremor, sampled every 9 ms, falls faster than the
            // default allowance at 5000.
            var engine = Engine();
            Signals.Run(engine, Signals.Ramp(0, 5000, 60));

            var events = Signals.Run(engine, Signals.SampleAndHold(Signals.Drag(5000, 5000, 0.30, 400, 0.03, 15, seed: 1)));

            Assert.Contains(events, e => e.Event == TriggerEvent.FastRelease);
        }

        [Theory]
        [InlineData(1500, 300, 70)]
        [InlineData(1500, 400, 100)]
        [InlineData(4000, 400, 70)]
        [InlineData(4000, 600, 100)]
        public void ShallowRePressesWithoutLiftingAreDetected(double low, double depth, double periodMs)
        {
            // Taps without lifting, sampled every 9 ms. With the fixed 20 units/ms allowance none of these released.
            var engine = Engine();
            Signals.Run(engine, Signals.Ramp(0, low, 20));

            var events = Signals.Run(engine, Signals.SampleAndHold(Signals.Taps(low, low + depth, 12, periodMs)));

            Assert.Equal(11, events.Count(e => e.Event == TriggerEvent.Rearm));
        }

        [Fact]
        public void ShallowRePressesAt4000SampledEvery5MsAreDetected()
        {
            // 450 deep every 120 ms from 4000 (peak fall ~11.8 units/ms around 4225): 2.5.1's allowance there is
            // 2 + 0.19% = 10.0 units/ms, so the steepest samples add up past 10; 2.5.0's (1.5 + 0.22% = 10.8) never did,
            // and every re-press ghosted through. Noise-free, 5 ms samples.
            static int RePresses(TriggerSettings s)
            {
                var engine = new TriggerEngine(s);
                Signals.Run(engine, Signals.Ramp(0, 4000, 20));
                return Signals.Run(engine, Signals.SampleAndHold(Signals.Taps(4000, 4450, 12, 120), 5)).Count(e => e.Event == TriggerEvent.Rearm);
            }

            Assert.Equal(11, RePresses(new TriggerSettings()));
            Assert.Equal(0, RePresses(new TriggerSettings { FastFallSpeed = 1.5, FastFallPercent = 0.22 }));
        }

        [Theory]
        // Hold dips from recordings/play-ptk670-20261001.csv that the previous version released on.
        [InlineData(new double[] { 7246, 7223, 7207, 7194, 7171, 7064, 7054, 7097, 7171, 7278 })]
        [InlineData(new double[] { 7119, 7113, 7051, 6946, 6826, 6726, 6649, 6630, 6694, 6759, 6753 })]
        [InlineData(new double[] { 8191, 8115, 8022, 7943, 7878, 7810, 7764, 7735, 7742, 7776, 7796 })]
        [InlineData(new double[] { 8191, 8184, 8100, 8023, 8004, 8069, 8141, 8186 })]
        public void RealHoldDipsSampledEvery9MsDoNotRelease(double[] samples)
        {
            var engine = Engine();
            Signals.Run(engine, Signals.Samples(9, 4000, samples[0], samples[0], samples[0]));

            var events = Signals.Run(engine, Signals.Samples(9, samples));

            Assert.Empty(events);
            Assert.True(engine.Pressed);
        }

        [Fact]
        public void RepeatedValuesAreNotNewSamples()
        {
            // A fall of 107 every 9 ms (12 units/ms) is slower than the 20 units/ms allowance. Counted per report
            // instead of per sample, it looked like 107 units in 1 ms.
            var engine = Engine();
            HoldAt(engine, 7200);

            Assert.Empty(Signals.Run(engine, Signals.Samples(9, 7200, 7093, 6986, 6879)));
        }

        [Fact]
        public void RealTapReleasesOnItsFirstFallingSample()
        {
            // A tap from the recording, one pressure sample per 9 reports: 2947 -> 2825 is 122 down in 9 ms, above the
            // allowance at 2947 (2 + 0.19% = 7.6 units/ms, 68 per sample) by more than 10.
            var engine = Engine();
            engine.Update(0, 1);

            var events = Signals.Run(engine, Signals.Samples(9, 2768, 2947, 2825, 2342, 1862, 1105, 0));

            Assert.Equal((0, TriggerEvent.Contact), events[0]);
            Assert.Equal((18, TriggerEvent.FastRelease), events[1]);
        }

        [Fact]
        public void RealTapSampledEvery5MsReleasesOnItsFirstFastSample()
        {
            // The end of a tap from recordings/play-ptk670-5ms-20261005.csv (t = 6.75 s). The first falls (8-35 per
            // sample) look like any hold; 5350 -> 5276 (74) is 13 over the allowance at 5350 (2 + 0.19% = 12.2 units/ms,
            // 61 per sample). 2.5.0 (1.5 + 0.22%, 66 per sample) released one sample later, at 5175.
            var engine = Engine();
            engine.Update(0, 1);

            var events = Signals.Run(engine, Signals.Samples(5, 5428, 5420, 5407, 5385, 5350, 5276, 5175, 5046, 4892, 0));

            Assert.Equal((0, TriggerEvent.Contact), events[0]);
            Assert.Equal((25, TriggerEvent.FastRelease), events[1]);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(750)]
        [InlineData(1000)]
        public void LightDragWithNoiseSampledEvery5MsDoesNotRelease(double level)
        {
            // Light drag: 10% dips over 300 ms, 1.5% tremor at 9 Hz, +-10 noise (sigma 5.8; the 5 ms recording shows ~3-5
            // on steady holds) per 5 ms sample. The fixed part of the allowance (2 units/ms, 10 per sample) carries
            // this; the 9 ms era default (0.5 units/ms, 2.5 per 5 ms sample) cut out here. At +-15 some still cut out.
            var engine = Engine();
            Signals.Run(engine, Signals.Ramp(0, level, 40));

            var events = Signals.Run(engine, Signals.SampleAndHold(Signals.Drag(level, 8000, 0.10, 300, 0.015, 10, seed: 3), 5));

            Assert.Empty(events);
            Assert.True(engine.Pressed);

            var old = Engine(new TriggerSettings { FastFallSpeed = 0.5, FastFallPercent = 0.25 });
            Signals.Run(old, Signals.Ramp(0, level, 40));
            Assert.Contains(Signals.Run(old, Signals.SampleAndHold(Signals.Drag(level, 8000, 0.10, 300, 0.015, 10, seed: 3), 5)),
                e => e.Event == TriggerEvent.FastRelease);
        }

        [Fact]
        public void DropoutFromMaxPressureToZeroIsIgnored()
        {
            // Pressed at max, one 5 ms sample of 0, back at max: the zero is a sensor dropout, the tip stays down.
            var engine = Engine();
            engine.Update(0, 1);
            Signals.Run(engine, Signals.Samples(5, 6000, 7500, 8191, 8191));

            var events = Signals.Run(engine, Signals.Samples(5, 0, 8191, 8191, 8100, 8191));

            Assert.Empty(events);
            Assert.True(engine.Pressed);
            Assert.Equal(0, engine.FallExcess);
        }

        [Fact]
        public void DropoutReturningBelowMaxIsJudgedFromMax()
        {
            // Max, 0, then 3000: the zero is ignored and the fall 8191 -> 3000 releases like any fast fall.
            var engine = Engine();
            engine.Update(0, 1);
            Signals.Run(engine, Signals.Samples(5, 7000, 8191));

            var events = Signals.Run(engine, Signals.Samples(5, 0, 3000, 3000));

            Assert.Equal((5, TriggerEvent.FastRelease), events.Single());
        }

        [Fact]
        public void LiftStraightFromMaxPressureReleasesAfterDropoutTime()
        {
            var engine = Engine();
            engine.Update(0, 1);
            Signals.Run(engine, Signals.Samples(5, 7000, 8191));

            var events = Signals.Run(engine, Signals.Samples(5, 0, 0, 0, 0));
            Assert.Equal((12, TriggerEvent.Lift), events.Single());

            var off = Engine(new TriggerSettings { DropoutTime = 0 });
            off.Update(0, 1);
            Signals.Run(off, Signals.Samples(5, 7000, 8191));
            Assert.Equal((0, TriggerEvent.Lift), Signals.Run(off, Signals.Samples(5, 0, 0)).Single());
        }

        [Fact]
        public void LiftFromBelowMaxPressureIsNotDelayed()
        {
            var engine = Engine();
            engine.Update(0, 1);
            Signals.Run(engine, Signals.Samples(5, 7000, 8169));

            Assert.Equal(TriggerEvent.Lift, engine.Update(0, 1));
        }

        [Fact]
        public void ClassicModeCutsOutOnTheSameDrag()
        {
            // Why the drift reference and drag speed allowance exist: peak-distance rapid trigger releases here.
            var engine = Engine(Classic());
            Signals.Run(engine, Signals.Ramp(0, 5000, 60));

            var events = Signals.Run(engine, Signals.Drag(5000, 5000, 0.30, 400, 0.03, 15, seed: 1));

            Assert.Contains(events, e => e.Event == TriggerEvent.Release);
        }

        [Fact]
        public void RapidTapsWithoutLiftingAreAllDetected()
        {
            // 12 taps at 60 ms between 1500 and 4500 (about 250 BPM 1/4 streams) never touching the lift threshold.
            // The first tap was already pressed by the contact on the way down to 1500.
            var engine = Engine();
            Assert.Contains(Signals.Run(engine, Signals.Ramp(0, 1500, 20)), e => e.Event == TriggerEvent.Contact);

            var events = Signals.Run(engine, Signals.Taps(1500, 4500, 12, 60));

            Assert.Equal(11, events.Count(e => e.Event == TriggerEvent.Rearm));
            Assert.Equal(12, events.Count(e => IsRelease(e.Event)));
        }

        [Fact]
        public void RearmNeedsActivationDistanceAboveTheTrough()
        {
            var engine = Engine();
            engine.Update(3000, 1);
            Assert.True(IsRelease(engine.Update(2700, 1)));
            engine.Update(2500, 1);
            // 20 + 0.5% of 2500 = 32.5.
            Assert.Equal(TriggerEvent.None, engine.Update(2532, 1));
            Assert.Equal(TriggerEvent.Rearm, engine.Update(2533, 1));
        }

        [Fact]
        public void WobbleWhileReleasingFromAHeavyHoldDoesNotRePress()
        {
            // The end of a 750 ms hold on the recording (t = 89.3 s): 6976 -> 7015 (+39) on the way down after the
            // release. A fixed Activation Distance of 39 or less turned that into a double click.
            var samples = new double[] { 7795, 7752, 7604, 7369, 7091, 6976, 6979, 7015, 7001, 6940,
                6552, 5901, 5257, 4605, 3951, 3456, 2983, 2483, 2000, 1814, 1351, 0 };

            var engine = Engine();
            Signals.Run(engine, Signals.Samples(9, 7796));
            var events = Signals.Run(engine, Signals.Samples(9, samples));

            Assert.Equal(TriggerEvent.FastRelease, events[0].Event);
            Assert.DoesNotContain(events, e => e.Event == TriggerEvent.Rearm);

            var fixedDistance = Engine(new TriggerSettings { ActivationDistance = 39, ActivationPercent = 0 });
            Signals.Run(fixedDistance, Signals.Samples(9, 7796));
            Assert.Contains(Signals.Run(fixedDistance, Signals.Samples(9, samples)), e => e.Event == TriggerEvent.Rearm);
        }

        [Fact]
        public void HoldMultiplierRaisesReleaseDistanceForLongPresses()
        {
            var settings = Classic();
            settings.HoldReleaseMultiplier = 2;
            settings.HoldTime = 100;
            var engine = Engine(settings);
            Signals.Run(engine, Signals.Hold(3000, 200));

            Assert.Equal(500, engine.CurrentReleaseDistance, 3);
            Assert.Equal(TriggerEvent.None, engine.Update(2600, 1));
            Assert.Equal(TriggerEvent.Release, engine.Update(2500, 1));
        }

        [Fact]
        public void ReportsInTheSameInstantAreHandled()
        {
            var engine = Engine();
            engine.Update(3000, 1);
            Assert.Equal(TriggerEvent.None, engine.Update(2995, 0));
            Assert.True(IsRelease(engine.Update(2700, 0)));
            Assert.False(double.IsNaN(engine.FallExcess));
            Assert.False(double.IsInfinity(engine.FallExcess));
        }

        [Fact]
        public void ResetReturnsToLiftedState()
        {
            var engine = Engine();
            engine.Update(3000, 1);
            engine.Reset();
            Assert.False(engine.Pressed);
            Assert.Equal(TriggerEvent.Contact, engine.Update(20, 1));
        }
    }
}
