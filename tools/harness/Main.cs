using System;
using System.IO;
using System.Linq;
using IK.Core;
public static class H {
    // repository root: IK_ROOT, or the current directory
    static readonly string Root = (Environment.GetEnvironmentVariable("IK_ROOT") ?? Directory.GetCurrentDirectory()).TrimEnd('/') + "/";
    static readonly string CH = Root + "assets/screenpack/chars/";
    static readonly int seedBase = int.Parse(Environment.GetEnvironmentVariable("IK_SEED") ?? "1"); static int seedN;
    static Fighter Load(string name, string def) {
        var src = new FileSource(CH + name);
        var chr = MugenCharacter.Load(src, def, loadSound: false);
        var cmd = CmdFile.Parse(src.Read(chr.CmdFile));
        var states = chr.LoadStates(src, cmd);
        var r = new Random(seedBase + (seedN++)); return new Fighter(chr, states, cmd, () => r.Next(1000));
    }
    public static void Main(string[] args) {
        Fighter.CommonZss = ZssFile.Parse(File.ReadAllBytes(Root + "engine/ikemen-go/data/common1.cns.zss"));
        Console.WriteLine("common zss states: " + Fighter.CommonZss.States.Count + " warn " + string.Join(",", ZssFile.Warnings));
        var stage = StageDefinition.Load(new FileSource(Root + "assets/screenpack/stages"), "kfm.def", false);
        string c1 = args.Length > 0 ? args[0] : "kfm_zss", c2 = args.Length > 1 ? args[1] : c1;
        var a = Load(c1, c1 + ".def"); var b = Load(c2, c2 + ".def");
        var ai1 = new CpuAI(a, 8, 1); var ai2 = new CpuAI(b, 8, 2);
        a.AiLevel = 8; b.AiLevel = 8;
        var e = new FightEngine(a, b, stage); e.AnnounceTime = 0;
        var tm = Environment.GetEnvironmentVariable("IK_TEAM");
        var ais = new System.Collections.Generic.Dictionary<Fighter, CpuAI> { [a] = ai1, [b] = ai2 };
        if (tm != null) {
            e.Teams = tm == "tag" ? TeamMode.Tag : TeamMode.Turns;
            var a2 = Load(c2, c2 + ".def"); var b2 = Load(c1, c1 + ".def");
            a2.AiLevel = b2.AiLevel = 8;
            ais[a2] = new CpuAI(a2, 8, 3); ais[b2] = new CpuAI(b2, 8, 4);
            e.SetTeam(0, new[] { a, a2 }); e.SetTeam(1, new[] { b, b2 });
            e.StartRound(1);
        }
        string lastK = ""; int same = 0;
        // IK_PERF=1: managed bytes allocated and time per tick, AI vs engine (dev.7)
        bool perf = Environment.GetEnvironmentVariable("IK_PERF") != null;
        var sw = System.Diagnostics.Stopwatch.StartNew(); long aiBytes = 0, engBytes = 0, aiTicks = 0, engTicks = 0, maxEng = 0; int perfN = 0;
        for (int t = 0; t < 99*60+400; t++) { if (e.MatchOver && e.State == RoundState.WinPose) break;
            if (tm != null) { var p0 = e.Players[0]; var p1 = e.Players[1]; e.Tick(ais[p0].Tick(p0,p1,e), ais[p1].Tick(p1,p0,e)); if (e.TaggedIn[0] != null || e.TaggedIn[1] != null) Console.WriteLine($"t{t} TAG side0 {e.TaggedIn[0]?.Id} side1 {e.TaggedIn[1]?.Id}"); a = e.Players[0]; b = e.Players[1]; }
            else if (perf) {
                long m0 = GC.GetAllocatedBytesForCurrentThread(); long w0 = sw.ElapsedTicks;
                var k1 = ai1.Tick(a,b,e); var k2 = ai2.Tick(b,a,e);
                long m1 = GC.GetAllocatedBytesForCurrentThread(); long w1 = sw.ElapsedTicks;
                e.Tick(k1, k2);
                long m2 = GC.GetAllocatedBytesForCurrentThread(); long w2 = sw.ElapsedTicks;
                if (t >= 60) { aiBytes += m1 - m0; engBytes += m2 - m1; aiTicks += w1 - w0; engTicks += w2 - w1; maxEng = Math.Max(maxEng, w2 - w1); perfN++; }
            }
            else e.Tick(ai1.Tick(a,b,e), ai2.Tick(b,a,e));
            if (Environment.GetEnvironmentVariable("IK_STUCK") != null) { var k = $"{a.StateNo} {a.PosX:F1} {b.StateNo} {b.PosX:F1} {a.Anim?.Time} {b.Anim?.Time}"; if (k == lastK) { if (++same == 300) Console.WriteLine($"STUCK t{t} {k} sp{e.SuperPauseTime} p{e.PauseTime} hpA{a.HitPauseTime} hpB{b.HitPauseTime} chars{e.Chars.Count} bindA{a.BindTimeLeft} bindB{b.BindTimeLeft}"); } else { same = 0; lastK = k; } }
            if (t % (args.Length>2?int.Parse(args[2]):300) == 0 && t < (args.Length>3?int.Parse(args[3]):99999)) Console.WriteLine($"t{t} A f{a.Facing} st{a.StateNo} pos{a.PosX:F0} life{a.Life} | B f{b.Facing} st{b.StateNo} ctrl{b.Ctrl}  pos{b.PosX:F0} life{b.Life} id{a.Id}/{b.Id} alive{e.Alive(0)}-{e.Alive(1)} round {e.RoundNo} {e.State} wins {e.Wins[0]}-{e.Wins[1]}" + (Environment.GetEnvironmentVariable("IK_DEBUG") != null ? $" | A wx{a.WorldX:F0} {a.Type} {a.Move} ctrl{a.Ctrl} B wx{b.WorldX:F0} {b.Type} {b.Move} nat{b.NoAutoTurn} | A anim{a.AnimNo} t{a.Anim?.Time} at{a.Anim?.AnimTime} hp{a.HitPauseTime} hs{a.Ghv.HitShakeTime} time{a.Time} st{a.StateTime} {a.LastTransition}" : ""));
        }
        if (perf && perfN > 0) {
            double tk = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            Console.WriteLine($"PERF ticks {perfN}  alloc/tick: ai {aiBytes / (double)perfN:F0} B  engine {engBytes / (double)perfN:F0} B  |  time/tick: ai {aiTicks / tk / perfN:F3} ms  engine {engTicks / tk / perfN:F3} ms (max {maxEng / tk:F2} ms)  gc0 {GC.CollectionCount(0)}");
        }
        Console.WriteLine("unknown triggers: " + string.Join(",", a.UnknownTriggers) + " | " + string.Join(",", b.UnknownTriggers)); Console.WriteLine("moves " + ai1.Moves.Count + "/" + ai2.Moves.Count);
        Console.WriteLine("unknown ctrls: " + string.Join(",", a.UnknownControllers) + " | " + string.Join(",", b.UnknownControllers)); Console.WriteLine(string.Join(" ", a.Character.Warnings) + string.Join(" ", b.Character.Warnings));
        Console.WriteLine("zss warnings: " + string.Join(",", ZssFile.Warnings.Distinct()));
        Console.WriteLine("maps: " + string.Join(",", a.Maps.Select(kv => kv.Key + "=" + kv.Value)));
    }
}
