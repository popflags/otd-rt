using System;
using System.Collections.Generic;
using System.Linq;
using RapidTrigger;

namespace RtTool
{
    public readonly record struct EventRecord(int Index, double Time, TriggerEvent Event, uint Pressure, double Anchor, double ReleaseDistance);

    public readonly record struct ReleaseTiming(int Index, TriggerEvent Event, double MsSinceFallStart, double DropFromPeak, double Peak);

    public sealed class ReplayResult
    {
        public ReplayResult(PressureLog log, TriggerSettings settings, List<EventRecord> events)
        {
            Log = log;
            Settings = settings;
            Events = events;
        }

        public PressureLog Log { get; }
        public TriggerSettings Settings { get; }
        public List<EventRecord> Events { get; }

        public int Count(TriggerEvent ev) => Events.Count(e => e.Event == ev);
        public int Presses => Events.Count(e => e.Event is TriggerEvent.Contact or TriggerEvent.Rearm);
    }

    public static class Analysis
    {
        public static ReplayResult Replay(PressureLog log, TriggerSettings settings)
        {
            var engine = new TriggerEngine(settings);
            var events = new List<EventRecord>();
            double previous = log.Time[0];

            for (int i = 0; i < log.Count; i++)
            {
                double t = log.Time[i];
                var ev = engine.Update(log.Pressure[i], t - previous);
                previous = t;
                if (ev != TriggerEvent.None)
                    events.Add(new EventRecord(i, t, ev, log.Pressure[i], engine.HoldReference, engine.CurrentReleaseDistance));
            }

            return new ReplayResult(log, settings, events);
        }

        /// <summary>
        /// Release latency: time from the start of the descent that led to the release, and how much pressure
        /// had been shed by then. The descent starts at the most recent peak that stands out from the samples
        /// before it; within that peak, the last sample close to its maximum is taken (end of a plateau).
        /// </summary>
        public static List<ReleaseTiming> ReleaseTimings(ReplayResult result)
        {
            var log = result.Log;
            var timings = new List<ReleaseTiming>();
            double noise = NoiseSigma(log);
            int pressStart = -1;

            foreach (var e in result.Events)
            {
                if (e.Event is TriggerEvent.Contact or TriggerEvent.Rearm)
                {
                    pressStart = e.Index;
                    continue;
                }

                if (pressStart < 0)
                    continue;

                double released = log.Pressure[e.Index];
                double peak = released;
                int windowStart = pressStart;
                for (int i = e.Index - 1; i >= pressStart; i--)
                {
                    peak = Math.Max(peak, log.Pressure[i]);
                    double prominence = Math.Max(4 * noise, 0.3 * (peak - released));
                    if (log.Pressure[i] < peak - prominence)
                    {
                        windowStart = i;
                        break;
                    }
                }

                double tolerance = Math.Max(3 * noise, peak * 0.01);
                int fallStart = windowStart;
                for (int i = windowStart; i <= e.Index; i++)
                {
                    if (log.Pressure[i] >= peak - tolerance)
                        fallStart = i;
                }

                timings.Add(new ReleaseTiming(e.Index, e.Event, log.Time[e.Index] - log.Time[fallStart], peak - released, peak));
                pressStart = -1;
            }

            return timings;
        }

        /// <summary>
        /// For recordings where the pen was held down the whole time (drags): a release counts as a cut-out
        /// if the tip presses again before pressure reaches the lift threshold, or if pressure does not reach
        /// the lift threshold within <paramref name="liftWindowMs"/> (the drag was dropped while still held).
        /// </summary>
        public static List<EventRecord> CutOuts(ReplayResult result, double liftWindowMs)
        {
            var log = result.Log;
            var cutOuts = new List<EventRecord>();
            var events = result.Events;
            double lift = result.Settings.LiftThreshold;

            for (int k = 0; k < events.Count; k++)
            {
                var e = events[k];
                if (e.Event is not (TriggerEvent.Release or TriggerEvent.FastRelease))
                    continue;

                int nextPress = k + 1 < events.Count ? events[k + 1].Index : int.MaxValue;
                bool lifted = false, cut = false;

                for (int i = e.Index + 1; i < log.Count; i++)
                {
                    if (log.Pressure[i] <= lift) { lifted = true; break; }
                    if (i >= nextPress) { cut = true; break; }
                    if (log.Time[i] - e.Time > liftWindowMs) { cut = true; break; }
                }

                if (cut && !lifted)
                    cutOuts.Add(e);
            }

            return cutOuts;
        }

        /// <summary>
        /// Intentional taps estimated from the raw signal: falls of at least max(minSwing, fraction * peak)
        /// or down to the lift threshold, between alternating extremes. Returns (peakIndex, troughIndex).
        /// </summary>
        public static List<(int Peak, int Trough)> Swings(PressureLog log, double lift, double minSwing, double fraction)
        {
            var swings = new List<(int, int)>();
            var p = log.Pressure;
            int hi = 0, lo = 0;
            bool falling = false;

            for (int i = 1; i < p.Length; i++)
            {
                if (!falling)
                {
                    if (p[i] > p[hi]) hi = i;
                    double need = Math.Max(minSwing, fraction * p[hi]);
                    if (p[hi] > lift && (p[hi] - (double)p[i] >= need || p[i] <= lift))
                    {
                        falling = true;
                        lo = i;
                    }
                }
                else
                {
                    if (p[i] < p[lo]) lo = i;
                    if (p[i] - (double)p[lo] >= Math.Max(minSwing, fraction * p[lo]) || (p[lo] <= lift && p[i] > lift))
                    {
                        swings.Add((hi, lo));
                        falling = false;
                        hi = i;
                    }
                }
            }

            if (falling)
                swings.Add((hi, lo));
            return swings;
        }

        /// <summary>Intentional swings whose peak was pressed but which never released before the trough.</summary>
        public static int MissedReleases(ReplayResult result, List<(int Peak, int Trough)> swings)
        {
            var pressed = PressedMask(result);
            var releases = result.Events.Where(e => e.Event is TriggerEvent.Release or TriggerEvent.FastRelease or TriggerEvent.Lift).Select(e => e.Index).ToArray();
            int missed = 0;

            foreach (var (peak, trough) in swings)
            {
                if (!pressed[peak])
                    continue;
                int k = Array.BinarySearch(releases, peak);
                if (k < 0) k = ~k;
                if (k >= releases.Length || releases[k] > trough)
                    missed++;
            }

            return missed;
        }

        public static bool[] PressedMask(ReplayResult result)
        {
            var mask = new bool[result.Log.Count];
            bool state = false;
            int next = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                while (next < result.Events.Count && result.Events[next].Index == i)
                {
                    state = result.Events[next].Event is TriggerEvent.Contact or TriggerEvent.Rearm;
                    next++;
                }
                mask[i] = state;
            }
            return mask;
        }

        /// <summary>
        /// Per-sample sensor noise (standard deviation) while the pen is down, estimated from the median
        /// absolute second difference. Smooth pressure movement barely affects second differences, so this
        /// measures noise rather than hand motion.
        /// </summary>
        public static double NoiseSigma(PressureLog log)
        {
            var p = log.Pressure;
            var second = new List<double>();
            for (int i = 1; i < p.Length - 1; i++)
            {
                if (p[i - 1] > 50 && p[i] > 50 && p[i + 1] > 50)
                    second.Add(Math.Abs((double)p[i + 1] - 2.0 * p[i] + p[i - 1]));
            }

            if (second.Count == 0)
                return 0;
            second.Sort();
            // MAD -> sigma for a normal distribution, divided by sqrt(6) for the second difference of white noise.
            return 1.4826 * PercentileSorted(second, 0.5) / Math.Sqrt(6);
        }

        public static double MedianInterval(PressureLog log)
        {
            if (log.Count < 2)
                return 0;
            var intervals = new List<double>(log.Count - 1);
            for (int i = 1; i < log.Count; i++)
                intervals.Add(log.Time[i] - log.Time[i - 1]);
            intervals.Sort();
            return PercentileSorted(intervals, 0.5);
        }

        public static double PercentileSorted(IReadOnlyList<double> sorted, double q)
        {
            if (sorted.Count == 0)
                return double.NaN;
            int index = (int)Math.Clamp(Math.Ceiling(q * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        public static double Percentile(IEnumerable<double> values, double q)
            => PercentileSorted(values.OrderBy(v => v).ToList(), q);
    }
}
