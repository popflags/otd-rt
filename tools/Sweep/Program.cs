// Scores settings on the recordings and synthetic signals; used to choose the defaults (see CLAUDE.md).
//
// dotnet run -c Release --project tools/Sweep -- [--ms=5] [--light-noise=10] [--medium] [--misses] [--nodef] "Key=v1|v2,Key=v|v" ...
//
// Each argument is a grid over TriggerSettings properties (unlisted ones keep their defaults). Columns:
//   5ms:   cut-outs on the 5 ms play log (stroke at 62.4 s is landing wobble), hold margin (largest factor the fast
//          detector can be made more sensitive by, dividing FastFallSpeed, FastFallPercent and FastReleaseDistance,
//          before a hold stroke >= 300 ms cuts out), mean lead before lift for taps / holds (ms, larger = earlier)
//   9ms:   cut-outs and hold margin on the two 2026-10-01 logs (old log stroke 68 at 67.3 s is intended; the newtip
//          log has 12 accepted re-presses, so its margin is always 1)
//   repress: synthetic re-presses without lifting caught (release before the trough and re-press before the next
//          peak; raised cosine, low 1500/4000, depth 200-2000, period 50-150 ms, +-15 noise) and mean trough->re-press ms
//   light: cut-outs on synthetic light drags (500/750/1000, 10% dips over 300 ms, 1.5% tremor at 9 Hz), or with
//          --medium on medium drags (2500/5000, 15% dips, 2% tremor); noise +-N per sample (--light-noise, default 10)
//   syn_drag: cut-outs on the harsh synthetic drags of tools/synth_logs.py, sampled every --ms
// Synthetic signals are sampled every --ms (default 5, the PTK-670's current pressure interval).
using System.Globalization;
using RapidTrigger;
using RtTool;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "recordings")))
    root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Run from inside the repository.");
string R = Path.Combine(root, "recordings") + Path.DirectorySeparatorChar;
var rec5 = PressureLog.Load(R + "play-ptk670-5ms-20261005.csv", 1000);
var rec9 = PressureLog.Load(R + "play-ptk670-20261001.csv", 1000);
var rec9b = PressureLog.Load(R + "play-ptk670-newtip-20261001.csv", 1000);
int SampleMs = 5;
double LightNoise = 10;
bool Medium = args.Contains("--medium");
bool ShowMisses = args.Contains("--misses");
foreach (var a in args)
{
    if (a.StartsWith("--ms=")) SampleMs = int.Parse(a[5..]);
    if (a.StartsWith("--light-noise=")) LightNoise = double.Parse(a[14..]);
}
var synDrag = Synth.HarshDrags(SampleMs);
var (repress, cycles) = Synth.Repress(SampleMs);
var light = Synth.Drags(SampleMs, LightNoise, Medium);

// Strokes (runs above lift) per log
static List<(int A, int B)> Strokes(PressureLog l) { var s = new List<(int, int)>(); int i = 0; while (i < l.Count) { if (l.Pressure[i] > 2) { int j = i; while (j < l.Count && l.Pressure[j] > 2) j++; s.Add((i, j)); i = j; } else i++; } return s; }
var strokes5 = Strokes(rec5); var strokes9 = Strokes(rec9); var strokes9b = Strokes(rec9b);

// Unintended cut-outs inside hold strokes (>= 300 ms); old log stroke 68 (t 67.2-67.6 s) is intended.
int HoldCuts(PressureLog l, List<(int A, int B)> st, TriggerSettings s, bool skip68)
{
    var cuts = Analysis.CutOuts(Analysis.Replay(l, s), 300);
    int n = 0;
    foreach (var c in cuts)
    {
        if (skip68 && c.Time > 67200 && c.Time < 67700) continue;
        var k = st.FindIndex(x => x.A <= c.Index && c.Index < x.B);
        if (k >= 0 && l.Time[st[k].B - 1] - l.Time[st[k].A] >= 300) n++;
    }
    return n;
}
int AllCuts(PressureLog l, TriggerSettings s, bool skip68) => Analysis.CutOuts(Analysis.Replay(l, s), 300).Count(c => !(skip68 && c.Time > 67200 && c.Time < 67700));

TriggerSettings Scale(TriggerSettings s, double d) { var c = s.Clone(); c.FastFallSpeed /= d; c.FastFallPercent /= d; c.FastReleaseDistance /= d; return c; }
// Largest d (sensitivity multiplier) that keeps every real hold intact.
double Margin(TriggerSettings s, PressureLog l, List<(int, int)> st, bool skip68)
{
    if (HoldCuts(l, st, Scale(s, 1), skip68) > 0) return 1;
    double lo = 1, hi = 4; if (HoldCuts(l, st, Scale(s, hi), skip68) == 0) return hi;
    for (int i = 0; i < 14; i++) { double m = Math.Sqrt(lo * hi); if (HoldCuts(l, st, Scale(s, m), skip68) == 0) lo = m; else hi = m; }
    return hi;
}

(double Tap, double Hold) Leads(PressureLog l, List<(int A, int B)> st, TriggerSettings s)
{
    var r = Analysis.Replay(l, s);
    var tap = new List<double>(); var hold = new List<double>();
    foreach (var (a, b) in st)
    {
        var rel = r.Events.LastOrDefault(e => e.Index >= a && e.Index <= b && e.Event is TriggerEvent.FastRelease or TriggerEvent.Release or TriggerEvent.Lift);
        if (rel.Time == 0 && rel.Index == 0) continue;
        double lead = b < l.Count ? l.Time[b] - rel.Time : 0;
        if (l.Time[b - 1] - l.Time[a] >= 300) hold.Add(lead); else tap.Add(lead);
    }
    return (tap.Average(), hold.Average());
}

(int Caught, double Delay) Repress(TriggerSettings s)
{
    var r = Analysis.Replay(repress, s);
    var rel = r.Events.Where(e => e.Event is TriggerEvent.FastRelease or TriggerEvent.Release).Select(e => e.Index).ToArray();
    var pr = r.Events.Where(e => e.Event is TriggerEvent.Rearm).Select(e => e.Index).ToArray();
    int caught = 0; double delay = 0;
    var misses = new SortedDictionary<string, (int NoRelease, int NoRepress)>();
    foreach (var (peak, trough, next, label) in cycles)
    {
        bool released = rel.Any(i => i > peak && i <= trough);
        int p = Array.Find(pr, i => i > trough && i <= next);
        if (released && p > 0) { caught++; delay += repress.Time[p] - repress.Time[trough]; }
        else
        {
            misses.TryGetValue(label, out var m);
            misses[label] = released ? (m.NoRelease, m.NoRepress + 1) : (m.NoRelease + 1, m.NoRepress);
        }
    }
    if (ShowMisses)
        foreach (var (label, m) in misses)
            Console.WriteLine($"      {label}: {m.NoRelease,2} no release, {m.NoRepress,2} no re-press (of 19)");
    return (caught, caught > 0 ? delay / caught : double.NaN);
}

var configs = new List<(string Name, TriggerSettings S)>();
var def = new TriggerSettings();
configs.Add(("defaults", def));
foreach (var a in args.Where(a => !a.StartsWith("--")))
{
    // name:Key=V,Key=V  or grid spec Key=v1|v2,Key=...
    var grid = new List<TriggerSettings> { def.Clone() };
    foreach (var kv in a.Split(','))
    {
        var p = kv.Split('='); var prop = typeof(TriggerSettings).GetProperty(p[0])!;
        grid = grid.SelectMany(g => p[1].Split('|').Select(v => { var c = g.Clone(); prop.SetValue(c, double.Parse(v)); return c; })).ToList();
    }
    foreach (var g in grid) configs.Add((Describe(g), g));
}
string Describe(TriggerSettings s) => $"FFS {s.FastFallSpeed} FFP {s.FastFallPercent} FRD {s.FastReleaseDistance} Act {s.ActivationDistance}+{s.ActivationPercent}% Rel {s.ReleaseDistance} tau {s.DriftTimeConstant}";

Console.WriteLine($"synthetic sampled every {SampleMs} ms; repress cycles {cycles.Count}; {(Medium ? "medium" : "light")} drags +-{LightNoise} noise");
Console.WriteLine("config | 5ms: cuts holdMargin tapLead holdLead | 9ms: cuts(old,newtip) margin(old,newtip) | repress caught delay | drag cuts | syn_drag cuts");
foreach (var (name, s) in configs.Skip(args.Contains("--nodef") ? 1 : 0))
{
    var (lt, lh) = Leads(rec5, strokes5, s);
    var (c, d) = Repress(s);
    Console.WriteLine($"{name,-70} | {AllCuts(rec5, s, false),2} {Margin(s, rec5, strokes5, false),5:F2} {lt,5:F1} {lh,5:F1} | {AllCuts(rec9, s, true),2},{AllCuts(rec9b, s, false),2}  {Margin(s, rec9, strokes9, true):F2},{Margin(s, rec9b, strokes9b, false):F2} | {c,4} {d,5:F1} | {light.Sum(l => AllCuts(l, s, false)),3} | {AllCuts(synDrag, s, false),3}");
}

static class Synth
{
    static PressureLog Make(string name, List<double> v, int ms)
    {
        var p = new uint[v.Count]; var t = new double[v.Count];
        for (int i = 0; i < v.Count; i++) { t[i] = i; p[i] = (uint)Math.Max(0, Math.Round(v[i - i % ms])); }
        return new PressureLog(name, t, p);
    }
    public static (PressureLog, List<(int Peak, int Trough, int Next, string Label)>) Repress(int ms)
    {
        var v = new List<double>(); var cyc = new List<(int, int, int, string)>(); var r = new Random(1);
        foreach (var low in new[] { 1500.0, 4000 })
        foreach (var depth in new[] { 200.0, 300, 400, 600, 800, 1000, 1500, 2000 })
        foreach (var period in new[] { 50, 70, 100, 150 })
        {
            for (int i = 0; i < 50; i++) v.Add(0);
            for (int i = 1; i <= 30; i++) v.Add((low + depth) * i / 30);
            for (int k = 0; k < 20; k++)
            {
                int start = v.Count;
                for (int i = 0; i < period; i++) v.Add(low + depth * 0.5 * (1 + Math.Cos(2 * Math.PI * i / period)) + (r.NextDouble() * 2 - 1) * 15);
                if (k < 19) cyc.Add((start, start + period / 2, start + period, $"low {low,4} depth {depth,4} period {period,3}"));
            }
            for (int i = 1; i <= 20; i++) v.Add((low + depth) * (1 - i / 20.0));
            for (int i = 0; i < 100; i++) v.Add(0);
        }
        return (Make("repress", v, ms), cyc);
    }
    // Same shapes as tools/synth_logs.py syn_drag.csv (four held drags with 20-35% dips), as separate logs.
    public static PressureLog HarshDrags(int ms)
    {
        var v = new List<double>(); for (int i = 0; i < 50; i++) v.Add(0);
        int seed = 0;
        foreach (var (level, dipF, dipMs, trem) in new[] { (5000.0, .30, 400, .03), (2500.0, .25, 300, .02), (7000.0, .20, 500, .03), (1200.0, .35, 250, .02) })
        {
            var r = new Random(seed++);
            for (int i = 1; i <= 60; i++) v.Add(level * i / 60);
            for (int i = 0; i < 4000; i++)
            {
                double phase = i % (dipMs * 2.5); double dip = phase < dipMs ? Math.Sin(Math.PI * phase / dipMs) : 0;
                v.Add(level * (1 - dipF * dip) + Math.Sin(2 * Math.PI * 9 * i / 1000.0) * trem * level + (r.NextDouble() * 2 - 1) * 15);
            }
            int n = Math.Max(10, (int)(level / 50));
            for (int i = 1; i <= n; i++) v.Add(level * (1 - (double)i / n));
            for (int i = 0; i < 200; i++) v.Add(0);
        }
        return Make("harsh", v, ms);
    }

    public static List<PressureLog> Drags(int ms, double noise, bool medium)
    {
        var logs = new List<PressureLog>(); int seed = 0;
        var shapes = medium ? new[] { (2500.0, 0.15, 0.02), (5000.0, 0.15, 0.02) } : new[] { (500.0, 0.10, 0.015), (750.0, 0.10, 0.015), (1000.0, 0.10, 0.015) };
        foreach (var (level, dipF, trem) in shapes)
        {
            var r = new Random(seed++); var v = new List<double>();
            for (int i = 0; i < 50; i++) v.Add(0);
            for (int i = 1; i <= 40; i++) v.Add(level * i / 40);
            for (int i = 0; i < 8000; i++)
            {
                double phase = i % 750; double dip = phase < 300 ? Math.Sin(Math.PI * phase / 300) : 0;
                v.Add(level * (1 - dipF * dip) + Math.Sin(2 * Math.PI * 9 * i / 1000.0) * trem * level + (r.NextDouble() * 2 - 1) * noise);
            }
            for (int i = 1; i <= 20; i++) v.Add(level * (1 - i / 20.0));
            for (int i = 0; i < 100; i++) v.Add(0);
            logs.Add(Make($"drag{level}", v, ms));
        }
        return logs;
    }
}
