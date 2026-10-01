using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;

namespace RapidTrigger
{
    /// <summary>
    /// Records every report to CSV on a background thread. The report thread only does a non-blocking
    /// channel write, so enabling diagnostics does not add file I/O latency to the pipeline.
    /// The CSV can be replayed and used for calibration with the RtTool project.
    /// </summary>
    internal sealed class DiagnosticsRecorder : IDisposable
    {
        private readonly struct Sample
        {
            public Sample(long ticks, uint raw, bool pressed, TriggerEvent ev, double anchor, double holdReference, double releaseDistance, double fallExcess)
            {
                Ticks = ticks;
                Raw = raw;
                Pressed = pressed;
                Event = ev;
                Anchor = (float)anchor;
                HoldReference = (float)holdReference;
                ReleaseDistance = (float)releaseDistance;
                FallExcess = (float)fallExcess;
            }

            public readonly long Ticks;
            public readonly uint Raw;
            public readonly bool Pressed;
            public readonly TriggerEvent Event;
            public readonly float Anchor;
            public readonly float HoldReference;
            public readonly float ReleaseDistance;
            public readonly float FallExcess;
        }

        public const string Header = "t_ms,raw,pressed,event,anchor,hold_reference,release_distance,fall_excess";

        private readonly Channel<Sample> _channel;
        private readonly Thread _writer;
        private readonly long _startTicks;

        public string FilePath { get; }

        public DiagnosticsRecorder(string directory)
        {
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, $"rt-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _startTicks = Stopwatch.GetTimestamp();
            _channel = Channel.CreateUnbounded<Sample>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            _writer = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "RapidTrigger diagnostics",
                Priority = ThreadPriority.BelowNormal,
            };
            _writer.Start();
        }

        public void Record(long ticks, uint raw, TriggerEngine engine, TriggerEvent ev)
            => _channel.Writer.TryWrite(new Sample(ticks, raw, engine.Pressed, ev, engine.Anchor, engine.HoldReference, engine.CurrentReleaseDistance, engine.FallExcess));

        private void WriteLoop()
        {
            var reader = _channel.Reader;
            var culture = CultureInfo.InvariantCulture;
            double tickToMs = 1000.0 / Stopwatch.Frequency;

            using var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(Header);

            try
            {
                while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    while (reader.TryRead(out var s))
                    {
                        writer.Write(((s.Ticks - _startTicks) * tickToMs).ToString("F3", culture));
                        writer.Write(',');
                        writer.Write(s.Raw.ToString(culture));
                        writer.Write(s.Pressed ? ",1," : ",0,");
                        writer.Write(((int)s.Event).ToString(culture));
                        writer.Write(',');
                        writer.Write(s.Anchor.ToString("F1", culture));
                        writer.Write(',');
                        writer.Write(s.HoldReference.ToString("F1", culture));
                        writer.Write(',');
                        writer.Write(s.ReleaseDistance.ToString("F1", culture));
                        writer.Write(',');
                        writer.WriteLine(s.FallExcess.ToString("F1", culture));
                    }
                    writer.Flush();
                }
            }
            catch (IOException)
            {
                // Disk full or file removed: stop recording, never take the driver down.
            }
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _writer.Join(TimeSpan.FromSeconds(2));
        }
    }
}
