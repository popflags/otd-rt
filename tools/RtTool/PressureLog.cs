using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RtTool
{
    /// <summary>
    /// A recorded pressure stream. Reads the plugin's diagnostics CSV (t_ms,raw,...), the legacy
    /// "Timestamp,X,Y,Pressure" log (DateTime ticks) and plain one-value-per-line logs.
    /// </summary>
    public sealed class PressureLog
    {
        public PressureLog(string name, double[] time, uint[] pressure)
        {
            Name = name;
            Time = time;
            Pressure = pressure;
        }

        public string Name { get; }

        /// <summary>Milliseconds from the first sample.</summary>
        public double[] Time { get; }

        public uint[] Pressure { get; }

        public int Count => Pressure.Length;

        public double Duration => Count == 0 ? 0 : Time[^1] - Time[0];

        public static PressureLog Load(string path, double plainRateHz)
        {
            var lines = File.ReadAllLines(path);
            var header = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
            var times = new List<double>();
            var values = new List<uint>();
            var culture = CultureInfo.InvariantCulture;

            if (header.StartsWith("t_ms", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2
                        && double.TryParse(parts[0], NumberStyles.Float, culture, out var t)
                        && uint.TryParse(parts[1], NumberStyles.Integer, culture, out var p))
                    {
                        times.Add(t);
                        values.Add(p);
                    }
                }
            }
            else if (header.StartsWith("Timestamp", StringComparison.OrdinalIgnoreCase))
            {
                int pressureColumn = Array.FindIndex(header.Split(','), c => c.Trim().Equals("Pressure", StringComparison.OrdinalIgnoreCase));
                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length > pressureColumn
                        && long.TryParse(parts[0], NumberStyles.Integer, culture, out var ticks)
                        && uint.TryParse(parts[pressureColumn], NumberStyles.Integer, culture, out var p))
                    {
                        times.Add(ticks / 10_000.0);
                        values.Add(p);
                    }
                }
            }
            else
            {
                double period = 1000.0 / plainRateHz;
                foreach (var line in lines)
                {
                    if (uint.TryParse(line.Trim(), NumberStyles.Integer, culture, out var p))
                    {
                        times.Add(values.Count * period);
                        values.Add(p);
                    }
                }
            }

            if (values.Count == 0)
                throw new InvalidDataException($"No pressure samples found in '{path}'.");

            double t0 = times[0];
            return new PressureLog(Path.GetFileName(path), times.Select(t => t - t0).ToArray(), values.ToArray());
        }
    }
}
