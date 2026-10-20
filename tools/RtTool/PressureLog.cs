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
        public PressureLog(string name, double[] time, uint[] pressure, double[]? hostTime = null, int[]? buttons = null)
        {
            Name = name;
            Time = time;
            Pressure = pressure;
            HostTime = hostTime ?? time;
            Buttons = buttons;
        }

        public string Name { get; }

        /// <summary>Milliseconds from the first sample.</summary>
        public double[] Time { get; }

        public uint[] Pressure { get; }

        /// <summary>
        /// PC clock in ms (t_ms of diagnostics logs, uncapped; the same as <see cref="Time"/> for other logs): the Touchdown
        /// Gate runs on it, like the plugin.
        /// </summary>
        public double[] HostTime { get; }

        /// <summary>
        /// Pen side buttons per report (status byte 2 bits 1-3 of the PTK-670 firmware's 0x1E reports, -1 = unknown), from the
        /// diagnostics / td2csv `buttons` column; null when the log has none. Replays with Analog Pen use its changes.
        /// </summary>
        public int[]? Buttons { get; }

        public int Count => Pressure.Length;

        public double Duration => Count == 0 ? 0 : Time[^1] - Time[0];

        public static PressureLog Load(string path, double plainRateHz)
        {
            var lines = File.ReadAllLines(path);
            var header = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
            var times = new List<double>();
            var hosts = new List<double>();
            var values = new List<uint>();
            var buttons = new List<int>();
            var culture = CultureInfo.InvariantCulture;

            if (header.StartsWith("t_ms", StringComparison.OrdinalIgnoreCase))
            {
                // Newer diagnostics also record engine_ms, the time the engine was given (capped, or the firmware sample
                // clock): replay with it so the replay sees the driver's timing.
                var columns = header.Split(',');
                int timeColumn = Array.FindIndex(columns, c => c.Trim().Equals("engine_ms", StringComparison.OrdinalIgnoreCase));
                if (timeColumn < 0)
                    timeColumn = 0;
                int buttonsColumn = Array.FindIndex(columns, c => c.Trim().Equals("buttons", StringComparison.OrdinalIgnoreCase));
                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length > Math.Max(1, timeColumn)
                        && double.TryParse(parts[timeColumn], NumberStyles.Float, culture, out var t)
                        && double.TryParse(parts[0], NumberStyles.Float, culture, out var host)
                        && uint.TryParse(parts[1], NumberStyles.Integer, culture, out var p))
                    {
                        times.Add(t);
                        hosts.Add(host);
                        values.Add(p);
                        if (buttonsColumn >= 0)
                            buttons.Add(parts.Length > buttonsColumn && int.TryParse(parts[buttonsColumn], NumberStyles.Integer, culture, out var b) ? b : -1);
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
            double[]? hostTime = hosts.Count == values.Count ? hosts.Select(h => h - hosts[0]).ToArray() : null;
            return new PressureLog(Path.GetFileName(path), times.Select(t => t - t0).ToArray(), values.ToArray(), hostTime,
                buttons.Count == values.Count ? buttons.ToArray() : null);
        }
    }
}
