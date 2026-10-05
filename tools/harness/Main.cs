using System;
using System.IO;
using System.Linq;
using IK.Core;
public static class H {
    // repository root: IK_ROOT, or the current directory
    static readonly string Root = (Environment.GetEnvironmentVariable("IK_ROOT") ?? Directory.GetCurrentDirectory()).TrimEnd('/') + "/";
    static readonly string CH = Root + "assets/screenpack/chars/";
    static Fighter Load(string name, string def) {
        var src = new FileSource(CH + name);
        var chr = MugenCharacter.Load(src, def, loadSound: false);
        var cmd = CmdFile.Parse(src.Read(chr.CmdFile));
        var states = chr.LoadStates(src, cmd);
        return new Fighter(chr, states, cmd, () => new Random().Next(1000));
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
        for (int t = 0; t < 99*60+400; t++) { if (e.MatchOver && e.State == RoundState.WinPose) break;
            e.Tick(ai1.Tick(a,b,e), ai2.Tick(b,a,e));
            if (t % (args.Length>2?int.Parse(args[2]):300) == 0 && t < (args.Length>3?int.Parse(args[3]):99999)) Console.WriteLine($"t{t} A f{a.Facing} st{a.StateNo} pos{a.PosX:F0} life{a.Life} | B f{b.Facing} st{b.StateNo} ctrl{b.Ctrl}  pos{b.PosX:F0} life{b.Life} round {e.RoundNo} {e.State} wins {e.Wins[0]}-{e.Wins[1]}");
        }
        Console.WriteLine("unknown triggers: " + string.Join(",", a.UnknownTriggers) + " | " + string.Join(",", b.UnknownTriggers)); Console.WriteLine("moves " + ai1.Moves.Count + "/" + ai2.Moves.Count);
        Console.WriteLine("unknown ctrls: " + string.Join(",", a.UnknownControllers) + " | " + string.Join(",", b.UnknownControllers)); Console.WriteLine(string.Join(" ", a.Character.Warnings) + string.Join(" ", b.Character.Warnings));
        Console.WriteLine("zss warnings: " + string.Join(",", ZssFile.Warnings.Distinct()));
        Console.WriteLine("maps: " + string.Join(",", a.Maps.Select(kv => kv.Key + "=" + kv.Value)));
    }
}
