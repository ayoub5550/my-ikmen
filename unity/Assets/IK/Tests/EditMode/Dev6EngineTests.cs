using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.6 gate for the engine: ZSS loops and switch (compiler.go `loopBlock` /
    /// `switchBlock`, bytecode.go `StateBlock.Run`), `ignorehitpause` during hit pause, and the
    /// statedef defaults (an omitted movetype is I — bytecode.go `newStateBytecode`).
    /// The numbers expected are written in the small state files right here.
    /// </summary>
    public class Dev6EngineTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");

        static MugenCharacter character;
        static CmdFile cmd;

        [OneTimeSetUp]
        public void Load() {
            character = MugenCharacter.Load(new FileSource(Chars), "kfm.def", loadSound: false);
            cmd = CmdFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cmd")));
        }

        static FightEngine Fight(string zss) {
            var cns = CnsFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cns")));
            cns.Merge(cmd.States);
            var extra = ZssFile.Parse(zss);
            cns.Merge(extra);
            var e = new FightEngine(new Fighter(character, cns, cmd, () => 500f), new Fighter(character, cns, cmd, () => 500f));
            e.AnnounceTime = 0;
            e.Tick(CmdKey.None, CmdKey.None);
            e.P1.PosX = -30; e.P2.PosX = 30;
            return e;
        }

        static void RunState(FightEngine e, int state, int ticks = 1) {
            e.P1.ChangeState(state);
            for (int i = 0; i < ticks; i++) e.Tick(CmdKey.None, CmdKey.None);
        }

        [Test]
        public void ForLoop_EndIsInclusiveAndTheVariableIsVisible() {
            var e = Fight(@"
[StateDef 8000; type: S; movetype: I; physics: S; ctrl: 0;]
persistent(0) if 1 {
    varSet{var(20): 0}
    for i = 1; 5; 1 {
        varAdd{var(20): $i}
    }
    for 0; 9 {
        varAdd{var(21): 1}
    }
    for k = 10; 1; -3 {
        varAdd{var(22): 1}
    }
    for k = 5; 1; 1 {
        varAdd{var(23): 1}
    }
}");
            RunState(e, 8000);
            Assert.AreEqual(15, e.P1.Vars[20], "1+2+3+4+5: the end value runs");
            Assert.AreEqual(10, e.P1.Vars[21], "two expressions: step 1, 0..9");
            Assert.AreEqual(4, e.P1.Vars[22], "10,7,4,1 counting down");
            Assert.AreEqual(0, e.P1.Vars[23], "a range that starts outside does not run");
        }

        [Test]
        public void WhileLoop_BreakAndContinue() {
            var e = Fight(@"
[StateDef 8001; type: S; movetype: I; physics: S; ctrl: 0;]
persistent(0) if 1 {
    let n = 0;
    while $n < 100 {
        let n = $n + 1;
        if $n % 2 = 0 { continue; }
        if $n > 9 { break; }
        varAdd{var(30): 1}
    }
    varSet{var(31): $n}
    while 1 { varAdd{var(32): 1} }
}");
            RunState(e, 8001);
            Assert.AreEqual(5, e.P1.Vars[30], "odd n from 1 to 9");
            Assert.AreEqual(11, e.P1.Vars[31], "break at the first odd n above 9");
            Assert.AreEqual(ZssFile.MaxLoop, e.P1.Vars[32], "an endless loop stops at MaxLoop like Ikemen");
        }

        [Test]
        public void Switch_PicksOneCaseWithoutFallThrough() {
            var e = Fight(@"
[StateDef 8002; type: S; movetype: I; physics: S; ctrl: 0;]
persistent(0) if 1 {
    switch 3 {
    case 1:
        varSet{var(40): 1}
    case 2; 3:
        varSet{var(40): 23}
    case 3:
        varSet{var(41): 1}
    default:
        varSet{var(42): 1}
    }
    switch 9 {
    case 1:
        varSet{var(43): 1}
    default:
        varSet{var(43): 99}
    }
}");
            RunState(e, 8002);
            Assert.AreEqual(23, e.P1.Vars[40], "`case 2; 3:` lists two values");
            Assert.AreEqual(0, e.P1.Vars[41], "only the first matching case runs");
            Assert.AreEqual(0, e.P1.Vars[42], "default is the last else");
            Assert.AreEqual(99, e.P1.Vars[43]);
            CollectionAssert.IsEmpty(ZssFile.Warnings.FindAll(w => w.Contains("not supported")));
        }

        [Test]
        public void IgnoreHitPause_RunsDuringHitPauseOnlyThoseControllers() {
            var e = Fight(@"
[StateDef 8003; type: S; movetype: I; physics: S; ctrl: 0;]
ignoreHitPause varAdd{var(50): 1}
varAdd{var(51): 1}
");
            RunState(e, 8003);
            Assert.AreEqual(1, e.P1.Vars[50]);
            Assert.AreEqual(1, e.P1.Vars[51]);
            e.P1.HitPauseTime = 5;
            for (int i = 0; i < 5; i++) e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(6, e.P1.Vars[50], "ignorehitpause ran in each of the 5 frozen frames");
            Assert.AreEqual(1, e.P1.Vars[51], "the rest of the state waited");
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(2, e.P1.Vars[51], "and runs again after the pause");
        }

        [Test]
        public void Statedef_OmittedMoveTypeIsIdle_SoTheFighterTurnsAround() {
            var e = Fight(@"
[StateDef 8004; type: S; movetype: A; physics: S; ctrl: 0;]
if time >= 1 { changeState{value: 8005; ctrl: 1} }
[StateDef 8005; physics: S;]
");
            RunState(e, 8004, 3);
            Assert.AreEqual(8005, e.P1.StateNo);
            Assert.AreEqual(MoveType.Idle, e.P1.Move, "no movetype in the statedef = I");
            Assert.AreEqual(StateType.Standing, e.P1.Type, "no type = S");
            e.P1.PosX = 60; e.P2.PosX = -60; e.P1.Facing = 1;
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(-1, e.P1.Facing, "idle with control: faces the opponent again");
        }

        [Test]
        public void KfmZaxis_CompilesItsLoops() {
            ZssFile.Warnings.Clear();
            var dir = Path.Combine(Repo, "assets", "screenpack", "chars", "kfm_zaxis");
            ZssFile.Parse(File.ReadAllBytes(Path.Combine(dir, "kfm.zss")));
            CollectionAssert.IsEmpty(ZssFile.Warnings.FindAll(w => w.Contains("not supported")), "for loops in -4 and +1 compile");
        }
    }
}
