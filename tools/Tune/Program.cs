// Scores settings on the 2 ms gameplay recordings (PTK-670 ring500 83a52e3c, 1000 Hz motion sync ON, Windows, counter-timed: a, b and
// the S4 League game log) and synthetic 2 ms drags; how the owner's settings were chosen (see CLAUDE.md).
//
// dotnet run -c Release --project tools/Tune -- [--top=N] [--all] [--margin] [--robust] [--dump=FILE] "Key=v1|v2,Key=v" ...
//
// Each argument is a grid over TriggerSettings properties; unlisted ones keep the owner's settings (Owner() below). The first two
// rows are the owner's settings before (Previous(), 2.9.0) and now (2.10.0). Configs with light-drag cut-outs at +-10 or +-15 are hidden unless --all.
// Columns:
//   L:       cut-outs on light drags 150-1000 (10 % dips over 300 ms, 1.5 % tremor at 9 Hz, the dip at a random phase against the
//            contact, 20 seeds per level), noise +-10, +-15 (and +-20 in brackets; the recordings' sigma is ~4 per sample)
//   M:       cut-outs on medium drags 2500 / 5000 (15 % dips, 2 % tremor, +-10, 10 seeds each); harsher than any real drag dip
//   clicks:  the 9 intended re-clicks without lifting in the recordings (fastest 10 ms fall >= 29 raw/ms, rebound >= 220): how many
//            release before the trough and re-press within 80 ms after it, mean ms the release comes before the trough (larger =
//            earlier), mean ms from the trough to the re-press (a miss counts 80)
//   slowRel: of the 17 slow drag dips (fastest 10 ms fall <= 14.7 raw/ms, pen stays down), how many release (must be 0)
//   dbl:     re-presses inside strokes that are not one of the re-clicks: at landing (<= 40 ms after the contact) / later
//            (two-stage lifts, wobbles on a plateau after a release, sensor glitches without Glitch Jump)
//   late:    contacts (pressure rising from <= 2, first sample below 8191) that pressed after their first sample
//   tap/hold: mean lead before lift of each stroke's final release (ms, larger = earlier), strokes < / >= 300 ms; tap p10
//   rep:     synthetic re-presses without lifting caught (of 1216, as tools/Sweep, at 2 ms) and mean trough -> re-press ms
//   margin:  (--margin) largest factor FastFallSpeed, FastFallPercent and FastReleaseDistance can be divided by before a slow drag
//            dip releases
// --robust: light-drag cut-outs per level 150 / 250 / 500 / 750 / 1000 / 1500 / 2000 at +-10 / 15 / 20 noise instead.
// --dump=FILE: the last config's events on both recordings.
using System.Collections.Concurrent;
using System.Globalization;
using RapidTrigger;
using RtTool;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "recordings")))
    root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Run from inside the repository.");
string R = Path.Combine(root, "recordings") + Path.DirectorySeparatorChar;
var real = new[] { "play-ptk670-2ms-a-20261019.csv", "play-ptk670-2ms-b-20261019.csv", "play-ptk670-2ms-game-20261019.csv" }
    .Select(f => PressureLog.Load(R + f, 1000)).ToArray();
int top = 40;
bool all = args.Contains("--all"), withMargin = args.Contains("--margin");
foreach (var a in args)
    if (a.StartsWith("--top=")) top = int.Parse(a[6..]);

List<PressureLog> LightSet(double noise) => new[] { 150.0, 250, 500, 750, 1000 }
    .SelectMany(l => Enumerable.Range(0, 20).Select(sd => Synth.Drag(2, l, 0.10, 0.015, noise, 1000 + sd))).ToList();
var light10 = LightSet(10); var light15 = LightSet(15); var light20 = LightSet(20);
var medium = new[] { 2500.0, 5000 }.SelectMany(l => Enumerable.Range(0, 10).Select(sd => Synth.Drag(2, l, 0.15, 0.02, 10, 2000 + sd))).ToList();
var (repress, cycles) = Synth.Repress(2);

// The owner's settings since 2.10.0 (Windows and Linux), and the 2.9.0 ones before.
TriggerSettings Owner() => new()
{
    ContactThreshold = 4, PhantomContactPressure = 8191, PhantomConfirmTime = 20, DropoutTime = 4, DropoutPressure = 8190, GlitchJump = 2500,
    LiftThreshold = 2, ActivationDistance = 10, ActivationPercent = 0, FastFallSpeed = 12, FastFallPercent = 0.2, FastReleaseDistance = 35,
    TapAllowance = 0.2, RepressAllowance = 0.3, PressHoldoff = 12, ReleaseDistance = 600, ReleaseRatio = 0, MaxReleaseDistance = 1000,
    DriftTimeConstant = 10, PressDriftTimeConstant = 0, HoldTime = 400, HoldReleaseMultiplier = 1,
};
TriggerSettings Previous()
{
    var s = Owner();
    s.DropoutTime = 3; s.GlitchJump = 0; s.ActivationPercent = 0.75; s.TapAllowance = 0.1; s.ReleaseDistance = 300;
    return s;
}

static List<(int A, int B)> Strokes(PressureLog l)
{
    var s = new List<(int, int)>(); int i = 0;
    while (i < l.Count) { if (l.Pressure[i] > 2) { int j = i; while (j < l.Count && l.Pressure[j] > 2) j++; s.Add((i, j)); i = j; } else i++; }
    return s;
}
var strokes = real.Select(Strokes).ToArray();

// Labelled in-stroke dips (recording 0 = a, 1 = b, 2 = game; engine_ms of the dip's peak and trough). Found with a zigzag (fall >= 250
// from a local peak, rebound >= 100 before the lift, not within 20 ms of the contact, one-sample glitches excluded) and classified by
// the fastest fall over 10 ms of samples: re-clicks >= 29 raw/ms with a rebound >= 220, slow drag dips <= 14.7 raw/ms.
var intended = new (int F, double Peak, double Trough)[] { (1, 42504, 42938), (1, 82390, 82850), (1, 113782, 114196), (1, 129708, 130048), (1, 150404, 150770), (1, 164348, 164700),
    (2, 20898, 21332), (2, 116052, 116088), (2, 228612, 229102) };
var slow = new (int F, double Peak, double Trough)[] { (0, 30468, 30600), (0, 46220, 46534), (0, 66414, 66836), (1, 24600, 24916), (1, 37476, 37638), (1, 88864, 89128), (1, 169658, 169946), (1, 177188, 177460),
    (2, 5258, 5598), (2, 11052, 11370), (2, 22592, 22982), (2, 73662, 74108), (2, 98242, 98734), (2, 129012, 129176), (2, 162932, 163222), (2, 220260, 220514), (2, 251476, 251654) };

static bool IsRelease(EventRecord e) => e.Event is TriggerEvent.FastRelease or TriggerEvent.Release;
int SlowReleased(TriggerSettings s, List<EventRecord>[] ev)
    => slow.Count(d => ev[d.F].Any(e => e.Time >= d.Peak && e.Time <= d.Trough + 20 && IsRelease(e)));

(int Land, int Spur, int Caught, double RelLead, double Rearm, int SlowRel, double TapLead, double TapP10, double HoldLead, int Late) RealScore(TriggerSettings s)
{
    var ev = real.Select(l => Analysis.Replay(l, s).Events).ToArray();
    int land = 0, spur = 0, caught = 0; double relLead = 0, rearm = 0;
    foreach (var d in intended)
    {
        var rel = ev[d.F].FirstOrDefault(e => e.Time >= d.Peak && e.Time <= d.Trough && IsRelease(e));
        var re = ev[d.F].FirstOrDefault(e => e.Time > d.Trough && e.Time <= d.Trough + 80 && e.Event == TriggerEvent.Rearm);
        if (rel.Event != TriggerEvent.None && re.Event != TriggerEvent.None) { caught++; relLead += d.Trough - rel.Time; rearm += re.Time - d.Trough; }
        else rearm += 80;
    }
    var tap = new List<double>(); var hold = new List<double>();
    for (int f = 0; f < real.Length; f++)
    {
        var l = real[f];
        foreach (var (a, b) in strokes[f])
        {
            var e = ev[f].Where(x => x.Index >= a && x.Index < b + 1).ToList();
            if (e.Count == 0) continue;
            var c = e.FirstOrDefault(x => x.Event == TriggerEvent.Contact);
            for (int k = 1; k < e.Count; k++)
                if (e[k].Event == TriggerEvent.Rearm && !intended.Any(d => d.F == f && e[k].Time > d.Trough && e[k].Time <= d.Trough + 80))
                {
                    if (c.Event == TriggerEvent.Contact && e[k].Time - c.Time <= 40) land++;
                    else spur++;
                }
            var r = e.LastOrDefault(x => IsRelease(x) || x.Event == TriggerEvent.Lift);
            if (r.Event == TriggerEvent.None || b >= l.Count) continue;
            (l.Time[b - 1] - l.Time[a] >= 300 ? hold : tap).Add(l.Time[b] - r.Time);
        }
    }
    int late = 0;
    for (int f = 0; f < real.Length; f++)
    {
        var l = real[f]; var presses = ev[f].Where(e => e.Event is TriggerEvent.Contact or TriggerEvent.Rearm).Select(e => e.Index).ToArray();
        for (int i = 1; i < l.Count; i++)
        {
            if (!(l.Pressure[i - 1] <= 2 && l.Pressure[i] > 2) || l.Pressure[i] >= 8191) continue;
            int k = Array.BinarySearch(presses, i);
            if (k < 0 && ~k < presses.Length && presses[~k] - i < 40 && l.Time[presses[~k]] > l.Time[i]) late++;
        }
    }
    tap.Sort();
    return (land, spur, caught, caught > 0 ? relLead / caught : 0, rearm / intended.Length, SlowReleased(s, ev), tap.Average(), tap[tap.Count / 10], hold.Average(), late);
}

int Cuts(IEnumerable<PressureLog> logs, TriggerSettings s) => logs.Sum(l => Analysis.CutOuts(Analysis.Replay(l, s), 300).Count);

(int, double) Repress(TriggerSettings s)
{
    var r = Analysis.Replay(repress, s);
    var rel = r.Events.Where(IsRelease).Select(e => e.Index).ToArray();
    var pr = r.Events.Where(e => e.Event is TriggerEvent.Rearm).Select(e => e.Index).ToArray();
    int caught = 0; double delay = 0;
    foreach (var (peak, trough, next) in cycles)
    {
        bool released = rel.Any(i => i > peak && i <= trough);
        int p = Array.Find(pr, i => i > trough && i <= next);
        if (released && p > 0) { caught++; delay += repress.Time[p] - repress.Time[trough]; }
    }
    return (caught, caught > 0 ? delay / caught : double.NaN);
}

TriggerSettings Scale(TriggerSettings s, double d) { var c = s.Clone(); c.FastFallSpeed /= d; c.FastFallPercent /= d; c.FastReleaseDistance /= d; return c; }
int SlowAt(TriggerSettings s) => SlowReleased(s, real.Select(l => Analysis.Replay(l, s).Events).ToArray());
double Margin(TriggerSettings s)
{
    if (SlowAt(s) > 0) return 1;
    double lo = 1, hi = 4;
    if (SlowAt(Scale(s, hi)) == 0) return hi;
    for (int i = 0; i < 10; i++) { double m = Math.Sqrt(lo * hi); if (SlowAt(Scale(s, m)) == 0) lo = m; else hi = m; }
    return lo;
}

var configs = new List<TriggerSettings> { Previous(), Owner() };
foreach (var a in args.Where(a => !a.StartsWith("--")))
{
    var grid = new List<TriggerSettings> { Owner() };
    foreach (var kv in a.Split(','))
    {
        var p = kv.Split('='); var prop = typeof(TriggerSettings).GetProperty(p[0]) ?? throw new ArgumentException($"Unknown setting {p[0]}");
        grid = grid.SelectMany(g => p[1].Split('|').Select(v => { var c = g.Clone(); prop.SetValue(c, double.Parse(v)); return c; })).ToList();
    }
    configs.AddRange(grid);
}
string Desc(TriggerSettings s) => $"FFS {s.FastFallSpeed} FFP {s.FastFallPercent} FRD {s.FastReleaseDistance} Tap {s.TapAllowance} Rep {s.RepressAllowance} HT {s.HoldTime} HO {s.PressHoldoff} Act {s.ActivationDistance}+{s.ActivationPercent} PD {s.PressDriftTimeConstant} Rel {s.ReleaseDistance} GJ {s.GlitchJump}/{s.DropoutTime}";

var dump = args.FirstOrDefault(a => a.StartsWith("--dump="));
if (dump != null)
{
    using var w = new StreamWriter(dump[7..]);
    for (int f = 0; f < real.Length; f++)
        foreach (var e in Analysis.Replay(real[f], configs[^1]).Events) w.WriteLine($"{f} {e.Time:F0} {e.Event} {e.Pressure}");
    return;
}
if (args.Contains("--robust"))
{
    foreach (var c in configs)
    {
        var line = new System.Text.StringBuilder(Desc(c) + " |");
        foreach (var noise in new[] { 10.0, 15, 20 })
        {
            var per = new[] { 150.0, 250, 500, 750, 1000, 1500, 2000 }
                .Select(level => Enumerable.Range(0, 20).Sum(sd => Analysis.CutOuts(Analysis.Replay(Synth.Drag(2, level, 0.10, 0.015, noise, 1000 + sd), c), 300).Count)).ToList();
            line.Append($" +-{noise}: {string.Join(" ", per)} ({per.Sum()}) |");
        }
        Console.WriteLine(line);
    }
    return;
}

// Ranking (hidden configs aside): every re-click caught first, then re-click release lead, tap and hold lead, fewer double clicks.
double Score((int Land, int Spur, int Caught, double RelLead, double Rearm, int SlowRel, double TapLead, double TapP10, double HoldLead, int Late) r)
    => r.SlowRel > 0 ? -1000 + r.Caught : r.Caught * 100 + r.RelLead + 2 * r.TapLead + r.HoldLead / 4 - 3 * (r.Land + r.Spur);
var results = new ConcurrentBag<(int Order, string Line, bool Ok, double Key)>();
Parallel.ForEach(configs.Select((c, i) => (c, i)), x =>
{
    var s = x.c;
    int l10 = Cuts(light10, s), l15 = Cuts(light15, s);
    bool ok = l10 == 0 && l15 == 0;
    if (!ok && !all && x.i > 1) { results.Add((x.i, "", false, 0)); return; }
    var rs = RealScore(s); var (rc, rd) = Repress(s);
    results.Add((x.i, $"{Desc(s),-92} | L {l10,3} {l15,3} ({Cuts(light20, s),3}) M {Cuts(medium, s),3} | clicks {rs.Caught}/{intended.Length} lead {rs.RelLead,5:F1} rearm {rs.Rearm,5:F1} | slowRel {rs.SlowRel} dbl {rs.Land,2} {rs.Spur,2} late {rs.Late} | tap {rs.TapLead,5:F1} p10 {rs.TapP10,4:F0} hold {rs.HoldLead,5:F1} | rep {rc,4} {rd,4:F1}{(withMargin ? $" | margin {Margin(s):F2}" : "")}", ok, Score(rs)));
});
Console.WriteLine($"{configs.Count} configs, {results.Count(r => r.Ok)} without light-drag cut-outs at +-10 / +-15");
Console.WriteLine("config | L +-10 +-15 (+-20) M | re-clicks caught, release ms before trough, re-press ms after | slow dips released, double clicks landing / later | lead before lift: tap mean, p10, hold mean | synthetic re-presses, delay");
foreach (var r in results.Where(r => r.Order <= 1).OrderBy(r => r.Order).Concat(results.Where(r => r.Order > 1 && (r.Ok || all)).OrderByDescending(r => r.Key).Take(top)))
    Console.WriteLine(r.Line);

static class Synth
{
    static PressureLog Make(string name, List<double> v, int ms)
    {
        var p = new uint[v.Count]; var t = new double[v.Count];
        for (int i = 0; i < v.Count; i++) { t[i] = i; p[i] = (uint)Math.Max(0, Math.Round(v[i - i % ms])); }
        return new PressureLog(name, t, p);
    }

    // Same as tools/Sweep: re-taps without lifting (raised cosine, low 1500 / 4000, depth 200-2000, period 50-150 ms, +-15 noise).
    public static (PressureLog, List<(int Peak, int Trough, int Next)>) Repress(int ms)
    {
        var v = new List<double>(); var cyc = new List<(int, int, int)>(); var r = new Random(1);
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
                if (k < 19) cyc.Add((start, start + period / 2, start + period));
            }
            for (int i = 1; i <= 20; i++) v.Add((low + depth) * (1 - i / 20.0));
            for (int i = 0; i < 100; i++) v.Add(0);
        }
        return (Make("repress", v, ms), cyc);
    }

    // 8 s drag at `level`: dips of dipF over 300 ms every 750 ms starting at a random phase, tremor at 9 Hz, uniform noise; the 40 ms
    // landing ramp ends on the drag's own starting value.
    public static PressureLog Drag(int ms, double level, double dipF, double trem, double noise, int seed)
    {
        var r = new Random(seed); var v = new List<double>();
        double ph0 = r.Next(750);
        double Clean(int i)
        {
            double phase = (i + ph0) % 750, dip = phase < 300 ? Math.Sin(Math.PI * phase / 300) : 0;
            return level * (1 - dipF * dip) + Math.Sin(2 * Math.PI * 9 * i / 1000.0) * trem * level;
        }
        for (int i = 0; i < 50; i++) v.Add(0);
        for (int i = 1; i <= 40; i++) v.Add(Clean(0) * i / 40);
        for (int i = 0; i < 8000; i++) v.Add(Clean(i) + (r.NextDouble() * 2 - 1) * noise);
        for (int i = 1; i <= 20; i++) v.Add(level * (1 - i / 20.0));
        for (int i = 0; i < 100; i++) v.Add(0);
        return Make($"drag{level}", v, ms);
    }
}
