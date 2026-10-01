using System;
using System.Collections.Generic;

namespace RapidTrigger.Tests
{
    /// <summary>Synthetic 1000 Hz pressure streams.</summary>
    internal static class Signals
    {
        public const double Period = 1.0;

        /// <summary>Linear ramp from <paramref name="from"/> to <paramref name="to"/> over <paramref name="ms"/>.</summary>
        public static IEnumerable<double> Ramp(double from, double to, double ms)
        {
            int n = Math.Max(1, (int)Math.Round(ms / Period));
            for (int i = 1; i <= n; i++)
                yield return from + (to - from) * i / n;
        }

        public static IEnumerable<double> Hold(double level, double ms)
        {
            for (int i = 0; i < (int)(ms / Period); i++)
                yield return level;
        }

        /// <summary>
        /// A drag: pressure held around <paramref name="level"/> with slow dips of <paramref name="dipFraction"/>
        /// lasting <paramref name="dipMs"/>, physiological tremor and sensor noise.
        /// </summary>
        public static IEnumerable<double> Drag(double level, double ms, double dipFraction, double dipMs, double tremorFraction, double noise, int seed)
        {
            var random = new Random(seed);
            for (int i = 0; i < (int)(ms / Period); i++)
            {
                double t = i * Period;
                double dipPhase = t % (dipMs * 2.5);
                double dip = dipPhase < dipMs ? Math.Sin(Math.PI * dipPhase / dipMs) : 0;
                double tremor = Math.Sin(2 * Math.PI * 9 * t / 1000) * tremorFraction * level;
                yield return level * (1 - dipFraction * dip) + tremor + (random.NextDouble() * 2 - 1) * noise;
            }
        }

        /// <summary>Rapid taps without lifting: raised-cosine pulses between <paramref name="low"/> and <paramref name="high"/>.</summary>
        public static IEnumerable<double> Taps(double low, double high, int count, double periodMs)
        {
            for (int k = 0; k < count; k++)
            {
                for (int i = 0; i < (int)(periodMs / Period); i++)
                {
                    double phase = i * Period / periodMs;
                    yield return low + (high - low) * 0.5 * (1 - Math.Cos(2 * Math.PI * phase));
                }
            }
        }

        public static List<(int Index, TriggerEvent Event)> Run(TriggerEngine engine, IEnumerable<double> signal, int startIndex = 0)
        {
            var events = new List<(int, TriggerEvent)>();
            int i = startIndex;
            foreach (double v in signal)
            {
                var ev = engine.Update((uint)Math.Max(0, Math.Round(v)), Period);
                if (ev != TriggerEvent.None)
                    events.Add((i, ev));
                i++;
            }
            return events;
        }
    }
}
