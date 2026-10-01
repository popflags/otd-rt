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
        public void LiftAlwaysReleases()
        {
            var engine = Engine(new TriggerSettings { ReleaseDistance = 100000, FastFallSpeed = 0 });
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
            // 18 units/ms for 30 ms: 540 units down, but never faster than the 20 units/ms allowance.
            var engine = Engine(new TriggerSettings { ReleaseDistance = 100000 });
            HoldAt(engine, 4000);

            var events = Signals.Run(engine, Signals.Ramp(4000, 3460, 30));

            Assert.Empty(events);
        }

        [Fact]
        public void SensorNoiseOnAStillHoldDoesNotRelease()
        {
            // +-25 uniform noise (sigma ~14) on a 3000 hold for 20 seconds at 1000 Hz.
            var engine = Engine();
            HoldAt(engine, 3000);

            var events = Signals.Run(engine, Signals.Drag(3000, 20000, 0, 400, 0, 25, seed: 7));

            Assert.Empty(events);
        }

        [Fact]
        public void SlowDipsTremorAndNoiseDuringADragDoNotRelease()
        {
            // 30% dips over 400 ms, +-3% tremor at 9 Hz and +-15 noise, for 5 seconds.
            var engine = Engine();
            Signals.Run(engine, Signals.Ramp(0, 5000, 60));

            var events = Signals.Run(engine, Signals.Drag(5000, 5000, 0.30, 400, 0.03, 15, seed: 1));

            Assert.Empty(events);
            Assert.True(engine.Pressed);
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
            Assert.Equal(TriggerEvent.None, engine.Update(2539, 1));
            Assert.Equal(TriggerEvent.Rearm, engine.Update(2540, 1));
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
            Assert.Equal(TriggerEvent.None, engine.Update(2990, 0));
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
