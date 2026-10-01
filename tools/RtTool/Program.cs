using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RapidTrigger;
using RtTool;

var culture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.CurrentCulture = culture;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

try
{
    var options = Options.Parse(args.Skip(1).ToArray());
    return args[0] switch
    {
        "replay" => Replay(options),
        "calibrate" => Calibrate(options),
        _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
    };
}
catch (Exception e) when (e is ArgumentException or System.IO.IOException or FormatException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        RtTool - replay and calibrate the Rapid Trigger plugin against recorded pressure logs.

        Record logs with "Enable Diagnostics" in the plugin (written to ~/rapid-trigger-logs).
        Legacy "Timestamp,X,Y,Pressure" logs and plain one-value-per-line logs are also accepted.

        rt replay <log>... [--set Name=Value]... [--events] [--rate Hz]
            Run the plugin logic over each log and print presses, releases, release timing,
            cut-outs (meaningful for drag recordings) and a pressure noise estimate.

        rt calibrate --drag <log> [--drag <log>]... [--tap <log>]... [--margin 0.35]
                     [--taus 0,5,10,15,20,30,50] [--fast-percents 0,0.15,0.2,0.25,0.3,0.4] [--set Name=Value]...
            Drag logs: the tip was meant to stay down for the whole drag, only lifting at the end.
            Tap logs:  rapid taps / streams, for measuring release speed.
            For each drift time constant / fast fall percent pair (0 = fast detector off), finds the smallest release distance with zero cut-outs
            across all drag logs, adds the margin, and reports release speed. Prints the
            recommended plugin settings.

        Setting names: ContactThreshold, LiftThreshold, ActivationDistance, ActivationPercent, ReleaseDistance,
        ReleaseRatio, MaxReleaseDistance, DriftTimeConstant, FastFallSpeed, FastFallPercent, FastReleaseDistance, PressDriftTimeConstant,
        HoldTime, HoldReleaseMultiplier.

        Other options:
            --lift-window ms   time after a release in which pressure must reach the lift
                               threshold, otherwise it counts as a cut-out (default 300)
            --rate Hz          sample rate for plain logs without timestamps (default 1000)
        """);
}

static int Replay(Options o)
{
    if (o.Files.Count == 0)
        throw new ArgumentException("replay needs at least one log file.");

    var settings = o.Settings;
    PrintSettings(settings);

    foreach (var file in o.Files)
    {
        var log = PressureLog.Load(file, o.Rate);
        var result = Analysis.Replay(log, settings);
        double interval = Analysis.MedianInterval(log);
        double noise = Analysis.NoiseSigma(log);

        Console.WriteLine();
        Console.WriteLine($"== {log.Name}: {log.Count} samples, {log.Duration / 1000:F1} s, median interval {interval:F2} ms ({1000 / interval:F0} Hz)");
        Console.WriteLine($"   pressure changes every {Analysis.MedianSampleInterval(log):F1} ms (median while pressed)");
        Console.WriteLine($"   sensor noise sigma {noise:F1} -> keep Activation Distance >= {RecommendedActivation(noise):F0}");
        Console.WriteLine($"   presses {result.Presses} (contact {result.Count(TriggerEvent.Contact)}, rearm {result.Count(TriggerEvent.Rearm)}), " +
                          $"releases {result.Count(TriggerEvent.FastRelease) + result.Count(TriggerEvent.Release) + result.Count(TriggerEvent.Lift)} " +
                          $"(fast {result.Count(TriggerEvent.FastRelease)}, distance {result.Count(TriggerEvent.Release)}, lift {result.Count(TriggerEvent.Lift)})");

        PrintTimings("   release timing", Analysis.ReleaseTimings(result));

        var leads = Analysis.LeadBeforeLift(result, o.LiftWindow);
        if (leads.Count > 0)
            Console.WriteLine($"   release lead before lift (ms, larger = earlier; strokes that end in a lift): p50 {Analysis.Percentile(leads, 0.5):F1}, " +
                              $"p10 {Analysis.Percentile(leads, 0.1):F1}, mean {leads.Average():F1}");

        var cutOuts = Analysis.CutOuts(result, o.LiftWindow);
        Console.WriteLine($"   cut-outs if this was a continuous drag: {cutOuts.Count}");
        foreach (var c in cutOuts.Take(10))
            Console.WriteLine($"      t={c.Time,9:F1} ms  pressure {c.Pressure,5}  reference {c.Anchor,7:F0}  release distance {c.ReleaseDistance,5:F0}");
        if (cutOuts.Count > 10)
            Console.WriteLine($"      ... {cutOuts.Count - 10} more");

        var swings = Analysis.Swings(log, settings.LiftThreshold, 400, 0.35);
        Console.WriteLine($"   taps estimated from the raw signal: {swings.Count}, missed releases: {Analysis.MissedReleases(result, swings)}");

        if (o.ShowEvents)
        {
            foreach (var e in result.Events)
                Console.WriteLine($"      {e.Time,10:F1} ms  {e.Event,-8} pressure {e.Pressure,5}  anchor {e.Anchor,7:F0}");
        }
    }

    return 0;
}

static int Calibrate(Options o)
{
    if (o.DragFiles.Count == 0)
        throw new ArgumentException("calibrate needs at least one --drag log (tip held down for the whole drag).");

    var drags = o.DragFiles.Select(f => PressureLog.Load(f, o.Rate)).ToList();
    var taps = o.TapFiles.Select(f => PressureLog.Load(f, o.Rate)).ToList();
    var baseSettings = o.Settings;

    double noise = drags.Concat(taps).Max(Analysis.NoiseSigma);
    double sampleInterval = drags.Concat(taps).Select(Analysis.MedianSampleInterval).Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Min();
    // Light end of the pressures in use: where the pressure-proportional allowance is smallest.
    double lightPressure = Analysis.Percentile(drags.Concat(taps).SelectMany(l => l.Pressure).Where(v => v > 50).Select(v => (double)v), 0.1);
    double minReleaseDistance = Math.Max(10 * noise, baseSettings.ActivationDistance);
    double activation = Math.Max(baseSettings.ActivationDistance, RecommendedActivation(noise));

    Console.WriteLine($"Drag logs: {string.Join(", ", drags.Select(d => d.Name))}");
    Console.WriteLine($"Tap logs:  {(taps.Count == 0 ? "(none - release speed measured on drag ends only)" : string.Join(", ", taps.Select(d => d.Name)))}");
    Console.WriteLine($"Sensor noise sigma: {noise:F1} -> Activation Distance {activation:F0}, release distance floor {minReleaseDistance:F0}; pressure sample interval {sampleInterval:F1} ms");
    Console.WriteLine($"Margin: x{1 + o.Margin:F2} on top of the smallest stable release distance");
    Console.WriteLine();
    Console.WriteLine("drift ms  fast %/ms | stable x | used x | rel.dist  ratio    max | drag-end release ms p50/p90 | tap release ms p50/p90 | missed taps");
    Console.WriteLine("--------------------+----------+--------+-------------------------+----------------------------+------------------------+------------");

    (double Score, TriggerSettings Settings)? best = null;

    var combinations = o.Taus.SelectMany(tau => o.FastPercents.Select(pct => (tau, pct)));
    foreach (var (tau, fastPercent) in combinations)
    {
        var tauSettings = baseSettings.Clone();
        tauSettings.DriftTimeConstant = tau;
        tauSettings.FastFallPercent = fastPercent;
        if (fastPercent == 0)
            tauSettings.FastFallSpeed = 0;
        tauSettings.ActivationDistance = activation;
        double fastFall = tauSettings.FastFallSpeed;
        string label = $"{tau,8:F0}  {(fastPercent == 0 ? "off" : fastPercent.ToString("0.###") + "%"),9}";

        double? stable = SmallestStableScale(drags, tauSettings, o.LiftWindow);
        if (stable is null)
        {
            Console.WriteLine($"{label} | unstable even at x64 - drags in these logs cannot be held with this setting");
            continue;
        }

        double used = stable.Value * (1 + o.Margin);
        var candidate = Scale(tauSettings, used);
        if (candidate.ReleaseDistance < minReleaseDistance)
        {
            used *= minReleaseDistance / candidate.ReleaseDistance;
            candidate = Scale(tauSettings, used);
        }
        // The fast detector integrates noise too: keep it clear of what noise alone can add up to. Each pressure
        // sample is first charged the drag allowance for the time since the previous one, which absorbs the noise
        // when samples are far apart (PTK-670: ~9 ms).
        double allowance = fastFall + tauSettings.FastFallPercent * 0.01 * lightPressure;
        candidate.FastReleaseDistance = Math.Max(candidate.FastReleaseDistance, 4 * noise - allowance * sampleInterval);

        var dragEnd = drags.SelectMany(d => Analysis.ReleaseTimings(Analysis.Replay(d, candidate))).Select(t => t.MsSinceFallStart).ToList();
        var tapTimes = new List<double>();
        int missed = 0;
        foreach (var tap in taps)
        {
            var result = Analysis.Replay(tap, candidate);
            tapTimes.AddRange(Analysis.ReleaseTimings(result).Select(t => t.MsSinceFallStart));
            missed += Analysis.MissedReleases(result, Analysis.Swings(tap, candidate.LiftThreshold, 400, 0.35));
        }

        string tapText = tapTimes.Count == 0 ? "-" : $"{Analysis.Percentile(tapTimes, 0.5):F1} / {Analysis.Percentile(tapTimes, 0.9):F1}";
        Console.WriteLine($"{label} | {stable,8:F2} | {used,6:F2} | {candidate.ReleaseDistance,8:F0} {candidate.ReleaseRatio,6:F3} {candidate.MaxReleaseDistance,6:F0} | " +
                          $"{Analysis.Percentile(dragEnd, 0.5),12:F1} / {Analysis.Percentile(dragEnd, 0.9),-11:F1} | {tapText,-22} | {(taps.Count == 0 ? "-" : missed.ToString())}");

        // Prefer settings that catch every tap, then the fastest p90 release.
        var speed = tapTimes.Count > 0 ? tapTimes : dragEnd;
        double score = missed * 1000 + Analysis.Percentile(speed, 0.9);
        if (!double.IsNaN(score) && (best is null || score < best.Value.Score))
            best = (score, candidate);
    }

    Console.WriteLine();
    if (best is null)
    {
        Console.WriteLine("No stable configuration found. Check that the drag logs only contain held drags.");
        return 2;
    }

    Console.WriteLine("Recommended plugin settings:");
    PrintSettings(best.Value.Settings);
    Console.WriteLine();
    Console.WriteLine("Record more drags (different speeds, directions, pressures) for a safer result:");
    Console.WriteLine("calibration can only guarantee stability for the kind of drags it has seen.");
    return 0;
}

static double? SmallestStableScale(List<PressureLog> drags, TriggerSettings settings, double liftWindow)
{
    bool Stable(double scale) => drags.All(d => Analysis.CutOuts(Analysis.Replay(d, Scale(settings, scale)), liftWindow).Count == 0);

    const double Lowest = 0.02, Highest = 64;
    if (!Stable(Highest))
        return null;
    if (Stable(Lowest))
        return Lowest;

    double lo = Lowest, hi = Highest;
    for (int i = 0; i < 30; i++)
    {
        double mid = Math.Sqrt(lo * hi);
        if (Stable(mid)) hi = mid; else lo = mid;
    }
    return hi;
}

static TriggerSettings Scale(TriggerSettings s, double scale)
{
    var scaled = s.Clone();
    scaled.ReleaseDistance = s.ReleaseDistance * scale;
    scaled.ReleaseRatio = s.ReleaseRatio * scale;
    scaled.MaxReleaseDistance = s.MaxReleaseDistance * scale;
    scaled.FastReleaseDistance = s.FastReleaseDistance * scale;
    return scaled;
}

// Six sigma keeps noise on a resting pen from re-pressing after a release.
static double RecommendedActivation(double noiseSigma) => Math.Ceiling(6 * noiseSigma);

static void PrintTimings(string label, List<ReleaseTiming> timings)
{
    if (timings.Count == 0)
    {
        Console.WriteLine($"{label}: no releases");
        return;
    }

    var ms = timings.Select(t => t.MsSinceFallStart).ToList();
    var drop = timings.Select(t => t.DropFromPeak).ToList();
    Console.WriteLine($"{label} (ms from start of descent): p50 {Analysis.Percentile(ms, 0.5):F1}, p90 {Analysis.Percentile(ms, 0.9):F1}, max {ms.Max():F1}" +
                      $" | pressure shed at release p50 {Analysis.Percentile(drop, 0.5):F0}, p90 {Analysis.Percentile(drop, 0.9):F0}");
}

static void PrintSettings(TriggerSettings s)
{
    foreach (var p in typeof(TriggerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.Name,-24} {p.GetValue(s):0.###}");
}

namespace RtTool
{
    internal sealed class Options
    {
        public List<string> Files { get; } = new();
        public List<string> DragFiles { get; } = new();
        public List<string> TapFiles { get; } = new();
        public TriggerSettings Settings { get; } = new();
        public List<double> Taus { get; private set; } = new() { 0, 5, 10, 15, 20, 30, 50 };
        public List<double> FastPercents { get; private set; } = new() { 0, 0.15, 0.2, 0.25, 0.3, 0.4 };
        public double Margin { get; private set; } = 0.35;
        public double LiftWindow { get; private set; } = 300;
        public double Rate { get; private set; } = 1000;
        public bool ShowEvents { get; private set; }

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
                double NextDouble() => double.Parse(Next(), CultureInfo.InvariantCulture);

                switch (args[i])
                {
                    case "--drag": o.DragFiles.Add(Next()); break;
                    case "--tap": o.TapFiles.Add(Next()); break;
                    case "--margin": o.Margin = NextDouble(); break;
                    case "--lift-window": o.LiftWindow = NextDouble(); break;
                    case "--rate": o.Rate = NextDouble(); break;
                    case "--events": o.ShowEvents = true; break;
                    case "--taus":
                        o.Taus = Next().Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToList();
                        break;
                    case "--fast-percents":
                        o.FastPercents = Next().Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToList();
                        break;
                    case "--set":
                        o.ApplySetting(Next());
                        break;
                    default:
                        if (args[i].StartsWith("--"))
                            throw new ArgumentException($"Unknown option '{args[i]}'.");
                        o.Files.Add(args[i]);
                        break;
                }
            }
            return o;
        }

        private void ApplySetting(string assignment)
        {
            var parts = assignment.Split('=', 2);
            var property = parts.Length == 2
                ? typeof(TriggerSettings).GetProperty(parts[0].Trim(), BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                : null;
            if (property is null)
                throw new ArgumentException($"Bad --set '{assignment}'. Use Name=Value with a TriggerSettings property name.");
            property.SetValue(Settings, double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
    }
}
