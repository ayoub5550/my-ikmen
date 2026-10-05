using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.5: Ikemen's ZSS state language. The screenpack's default roster
    /// (`data/select.def`) is kfm_zss, kfm720 and kfm_zaxis, two of which are written in ZSS,
    /// so the game is not complete without it. Expectations come from `chars/kfm_zss/*.zss`.
    /// </summary>
    public class ZssTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string CharDir(string c) => Path.Combine(Repo, "assets", "screenpack", "chars", c);

        [Test]
        public void Compiler_FlattensIfElseLetAndFunctions() {
            var z = ZssFile.Parse(@"
[Function Twice(v) r]
let r = $v * 2;

[StateDef 900; type: S; movetype: A; anim: 200; ctrl: 0;]
let a = call Twice(21);
if $a = 42 {
    varSet{var(1): 1}
} else if time > 5 {
    varSet{var(2): 1}
} else {
    varSet{var(3): 1}
}
persistent(0) if 1 { velAdd{x: 1} }   # comment
");
            var s = z.Get(900);
            Assert.IsNotNull(s);
            Assert.AreEqual("S", s.Get("type"));
            Assert.AreEqual("200", s.Get("anim"));
            int lets = 0, varsets = 0;
            foreach (var c in s.Controllers) { if (c.Type == ZssFile.LetType) lets++; if (c.Type == "varset") varsets++; }
            Assert.AreEqual(2, lets, "the function's let and the caller's let");
            Assert.AreEqual(3, varsets);
            var last = s.Controllers[s.Controllers.Count - 1];
            Assert.AreEqual("veladd", last.Type);
            Assert.AreEqual(0, last.Persistent);
        }

        static Fighter Load(string name, string def) {
            var src = new FileSource(CharDir(name));
            var chr = MugenCharacter.Load(src, def, loadSound: false);
            var cmd = CmdFile.Parse(src.Read(chr.CmdFile));
            var states = chr.LoadStates(src, cmd);
            return new Fighter(chr, states, cmd, () => 500f);
        }

        [Test]
        public void KfmZss_LoadsAndFights() {
            var a = Load("kfm_zss", "kfm_zss.def");
            Assert.IsNotNull(a.States.Get(200), "kfm.zss defines the light punch");
            Assert.IsNotNull(a.States.Get(-1), "command.zss / AI.zss define state -1");
            Assert.AreEqual(1000, a.Const.Life, "kfm.const [Data] life");
            var b = Load("kfm_zss", "kfm_zss.def");
            var e = new FightEngine(a, b);
            e.AnnounceTime = 0;
            e.Tick(CmdKey.None, CmdKey.None);
            a.PosX = -18; b.PosX = 18;
            int life = b.Life;
            e.Tick(CmdKey.x, CmdKey.None);
            for (int i = 0; i < 20; i++) e.Tick(CmdKey.None, CmdKey.None);
            Assert.Less(b.Life, life, "the ZSS light punch connects");
            CollectionAssert.IsEmpty(a.UnknownControllers, "every sctrl kfm_zss used is known");
        }

        [Test]
        public void KfmZss_OwnAiDrivesItAtAiLevel() {
            var a = Load("kfm_zss", "kfm_zss.def");
            var b = Load("kfm_zss", "kfm_zss.def");
            a.AiLevel = 8; b.AiLevel = 8;
            var e = new FightEngine(a, b);
            e.AnnounceTime = 0;
            int guard = 0;
            while (!e.RoundOver && guard++ < 99 * 60 + 100) e.Tick(CmdKey.None, CmdKey.None);
            Assert.IsTrue(a.Life < a.LifeMax || b.Life < b.LifeMax, "AI.zss fights by itself (no keys pressed)");
        }

        [Test]
        public void KfmZaxisAnd720_Load() {
            var z = Load("kfm_zaxis", "kfm_zaxis.def");
            Assert.IsNotNull(z.States.Get(200));
            var k = Load("kfm720", "kfm720.def");
            Assert.IsNotNull(k.States.Get(200));
            Assert.AreEqual(1280f, k.Character.LocalCoordWidth, 1f);
        }
    }
}
