using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.5 gate for the engine: redirection, helpers, explods, hit sparks, projectiles,
    /// SuperPause, palette effects, afterimages, hit modifiers, sounds and the CPU.
    ///
    /// Expectations come from Kung Fu Man's own files (`kfm.cns`: Explod in state 191,
    /// SuperPause in state 3000, `sparkno = 2` / `hitsound = 5, 0` of the light punch) or from
    /// small state files written inside the test, whose numbers are visible right here.
    /// </summary>
    public class Dev5EngineTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");
        static string Data => Path.Combine(Repo, "assets", "screenpack", "data");

        static MugenCharacter character;
        static CmdFile cmd;
        static AirFile fxAir;

        // test-only states: a helper, a projectile and the modifiers
        const string ExtraStates = @"
[Statedef 7000]
type = S
movetype = I
physics = S
anim = 0
ctrl = 0
[State 7000, spawn]
type = Helper
trigger1 = 1
persistent = 0
id = 77
stateno = 7100
pos = 30, -10
postype = p1
[State 7000, back]
type = ChangeState
trigger1 = Time >= 2
value = 0
ctrl = 1

[Statedef 7100]
type = A
movetype = I
physics = N
anim = 0
velset = 1, 0
[State 7100, mark parent]
type = ParentVarSet
trigger1 = 1
persistent = 0
v = 10
value = 42
[State 7100, gone]
type = DestroySelf
trigger1 = Time >= 5

[Statedef 7200]
type = S
movetype = A
physics = S
anim = 0
ctrl = 0
[State 7200, fire]
type = Projectile
trigger1 = 1
persistent = 0
projid = 9
projanim = 200
offset = 20, 0
velocity = 6, 0
attr = S, SP
damage = 40
hitflag = MAF
sparkno = 2
hitsound = 5, 0
projremovetime = 200
[State 7200, back]
type = ChangeState
trigger1 = Time >= 3
value = 0
ctrl = 1

[Statedef 7300]
type = S
movetype = I
physics = S
anim = 0
[State 7300, armour]
type = NotHitBy
trigger1 = 1
value = SCA
time = 1
[State 7300, glow]
type = PalFX
trigger1 = 1
persistent = 0
time = 30
add = 64, 0, 0
mul = 256, 256, 256
[State 7300, trail]
type = AfterImage
trigger1 = 1
persistent = 0
time = 40
length = 10
";

        [OneTimeSetUp]
        public void Load() {
            var source = new FileSource(Chars);
            character = MugenCharacter.Load(source, "kfm.def", loadSound: false);
            cmd = CmdFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cmd")));
            fxAir = AirFile.Parse(File.ReadAllBytes(Path.Combine(Data, "fightfx.air")));
        }

        static CnsFile States() {
            var cns = CnsFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cns")));
            cns.Merge(cmd.States);
            cns.Merge(CnsFile.Parse(ExtraStates));
            return cns;
        }

        static FightEngine NewFight(float distance = 60f) {
            var cns = States();
            var e = new FightEngine(new Fighter(character, cns, cmd, () => 500f), new Fighter(character, cns, cmd, () => 500f));
            e.FxAir = fxAir;
            e.AnnounceTime = 0;
            e.Tick(CmdKey.None, CmdKey.None);
            e.P1.PosX = -distance / 2f; e.P2.PosX = distance / 2f;
            e.P1.Facing = 1; e.P2.Facing = -1;
            return e;
        }

        static void Run(FightEngine e, int ticks, CmdKey p1 = CmdKey.None, CmdKey p2 = CmdKey.None) {
            for (int i = 0; i < ticks; i++) e.Tick(p1, p2);
        }

        // ---- redirection ----------------------------------------------------------------

        class Ctx : IRedirectContext {
            public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
            public Ctx Root;
            public bool TryTrigger(string name, string arg, float argValue, out float value) {
                return Values.TryGetValue(name.ToLowerInvariant() + (name == "var" ? "(" + argValue + ")" : ""), out value);
            }
            public float Random999() => 0;
            public IExprContext Redirect(string kind, int id, bool hasId) => kind == "root" ? Root : null;
        }

        [Test]
        public void Redirection_ReadsTheOtherCharacter() {
            var root = new Ctx(); root.Values["var(1)"] = 3; root.Values["life"] = 500;
            var me = new Ctx { Root = root }; me.Values["var(1)"] = 7; me.Values["life"] = 100;
            Assert.AreEqual(1f, Expr.Parse("root, var(1) = 3").Eval(me));
            Assert.AreEqual(1f, Expr.Parse("var(1) = 7 && root,life > 400").Eval(me));
            Assert.AreEqual(0f, Expr.Parse("parent, life").Eval(me), "missing redirection target is 0");
            Assert.AreEqual(503f, Expr.Parse("root,life + root,var(1)").Eval(me));
        }

        [Test]
        public void HitDefAttrList_KeepsEveryAttribute() {
            var e = NewFight();
            e.P1.ChangeState(200);      // KFM light punch: attr = S, NA
            Run(e, 4);
            Assert.IsTrue(e.P1.HitDefActive, "state 200 has set its HitDef by AnimElem 3");
            Assert.AreEqual(1f, Expr.Parse("hitdefattr = SCA, NA, SA").Eval(e.P1), "NA is in the list");
            Assert.AreEqual(0f, Expr.Parse("hitdefattr = SCA, SA, HA").Eval(e.P1), "S, NA is not a special/hyper");
        }

        // ---- animation instances ------------------------------------------------------------

        [Test]
        public void TwoFightersOfOneCharacter_HaveTheirOwnAnimationClock() {
            var e = NewFight();
            e.P1.ChangeState(200);
            Run(e, 3);
            Assert.AreNotSame(e.P1.Anim, e.P2.Anim, "each fighter owns its playback");
            Assert.AreEqual(0, e.P2.AnimNo);
            Assert.AreNotSame(character.Air.Get(e.P1.AnimNo), e.P1.Anim, "the .air action itself is never ticked");
        }

        // ---- helpers ------------------------------------------------------------------------

        [Test]
        public void Helper_SpawnsRunsItsStatesAndDestroysItself() {
            var e = NewFight();
            e.P1.ChangeState(7000);
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(3, e.Chars.Count, "two players and one helper");
            var h = e.Chars[2];
            Assert.IsTrue(h.IsHelper);
            Assert.AreEqual(77, h.HelperId);
            Assert.AreSame(e.P1, h.Root);
            Assert.AreEqual(e.P1.PosX + 30f, h.PosX, 1.5f, "pos = 30,-10 relative to the parent's facing");
            Assert.AreEqual(1f, Expr.Parse("numhelper(77)").Eval(e.P1));
            Assert.AreEqual(1f, Expr.Parse("helper(77), ishelper(77)").Eval(e.P1));
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(42, e.P1.Vars[10], "ParentVarSet wrote var(10) of the parent");
            Run(e, 8);
            Assert.AreEqual(2, e.Chars.Count, "DestroySelf at Time >= 5 removed the helper");
            Assert.AreEqual(0f, Expr.Parse("numhelper(77)").Eval(e.P1));
        }

        // ---- explods and sparks ---------------------------------------------------------------

        [Test]
        public void Explod_KfmWoodPiecesFlyAndExpire() {
            var e = NewFight();
            e.P1.ChangeState(191);          // the intro with the wood pieces (kfm.cns state 191)
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.GreaterOrEqual(e.Explods.Count, 1, "Wood 1: Explod anim 191 on the first tick");
            var wood = e.Explods[0];
            float y0 = wood.PosY;
            Run(e, 10);
            Assert.Less(wood.PosY, y0, "velocity = -4.2, -7 moves it up");
            Run(e, 60);
            Assert.IsFalse(e.Explods.Contains(wood), "removetime = 48 removed it");
        }

        [Test]
        public void Punch_MakesAFightfxSparkAndQueuesTheCommonHitSound() {
            var e = NewFight(36f);
            bool spark = false, sound = false;
            e.Tick(CmdKey.x, CmdKey.None);
            for (int i = 0; i < 20 && !spark; i++) {
                foreach (var x in e.Explods) if (x.IsSystem && x.Source == SpriteSource.FightFx && x.AnimNo == 0) spark = true;
                foreach (var s in e.Sounds) if (s.Common && s.Group == 5 && s.Index == 0) sound = true;
                if (!spark) e.Tick(CmdKey.None, CmdKey.None);
            }
            Assert.IsTrue(spark, "state 200 HitDef sparkno = 0 → fightfx action 0");
            Assert.IsTrue(sound, "hitsound = 5, 0 comes from fight.snd");
        }

        // ---- projectiles ----------------------------------------------------------------------

        [Test]
        public void Projectile_FliesHitsAndRecordsProjHit() {
            var e = NewFight(160f);
            int life = e.P2.Life;
            e.P1.ChangeState(7200);
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(1, e.Projectiles.Count, "Projectile spawned");
            Assert.AreEqual(1f, Expr.Parse("numprojid(9)").Eval(e.P1));
            int guard = 0;
            while (e.P2.Life == life && guard++ < 80) e.Tick(CmdKey.None, CmdKey.None);
            Assert.Less(e.P2.Life, life, "the projectile hit P2");
            Assert.AreEqual(1f, Expr.Parse("projhit9 = 1").Eval(e.P1), "ProjHit9 is set");
            Run(e, 60);
            Assert.AreEqual(0, e.Projectiles.Count, "projhits = 1: gone after the hit");
        }

        // ---- superpause -------------------------------------------------------------------------

        [Test]
        public void SuperPause_FreezesTheOpponentAndCostsPower() {
            var e = NewFight();
            e.P1.Power = 3000;
            e.P1.ChangeState(3000);       // kfm.cns: SuperPause on AnimElem = 2, poweradd = -1000
            int guard = 0;
            while (e.SuperPauseTime == 0 && guard++ < 60) e.Tick(CmdKey.None, CmdKey.None);
            Assert.Greater(e.SuperPauseTime, 0, "SuperPause started");
            Assert.AreEqual(2000, e.P1.Power, "poweradd = -1000");
            int p2Time = e.P2.StateTime;
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(p2Time, e.P2.StateTime, "P2 is frozen during the superpause");
            bool flash = false;
            foreach (var x in e.Explods) if (x.AnimNo == 100 && x.Source == SpriteSource.FightFx) flash = true;
            Assert.IsTrue(flash, "anim = 100 from fightfx");
        }

        // ---- modifiers ----------------------------------------------------------------------------

        [Test]
        public void NotHitBy_PalFx_AfterImage() {
            var e = NewFight(36f);
            e.P2.ChangeState(7300);
            int life = e.P2.Life;
            e.Tick(CmdKey.x, CmdKey.None);
            Run(e, 15);
            Assert.AreEqual(life, e.P2.Life, "NotHitBy SCA: the punch does not connect");
            Assert.IsTrue(e.P2.PalFx.Active, "PalFX time = 30");
            Assert.Greater(e.P2.After.Frames.Count, 3, "AfterImage records frames");
            var add = new float[3]; var mul = new float[3]; float sat; bool inv;
            e.P2.PalFx.Current(add, mul, out sat, out inv);
            Assert.AreEqual(0.25f, add[0], 0.001f, "add = 64 → 64/256");
        }

        [Test]
        public void KfmUsesOnlyKnownControllers() {
            // run every KFM state once: nothing may be reported as unknown
            foreach (var s in States().States) {
                if (s.No < 0 || s.No >= 7000) continue;
                var f = NewFight();
                f.P1.ChangeState(s.No);
                Run(f, 3);
                CollectionAssert.IsEmpty(f.P1.UnknownControllers, "state " + s.No);
            }
        }

        // ---- the CPU --------------------------------------------------------------------------------

        [Test]
        public void CpuAI_LearnsKfmMovesFromTheCmd() {
            var e = NewFight();
            var ai = new CpuAI(e.P1, 5);
            Assert.Greater(ai.Moves.Count, 8, "kfm.cmd links many commands to attack states");
            var punch = ai.Moves.Find(m => m.Command == "x" && m.StateNo == 200);
            Assert.IsNotNull(punch, "x → state 200");
            Assert.Greater(punch.Reach, 20f, "the punch has Clsn1 boxes in front");
            Assert.IsTrue(ai.Moves.Exists(m => m.IsSpecial), "QCF/QCB specials are known");
        }

        [Test]
        public void CpuAI_GeneratedKeysCompleteTheCommand() {
            foreach (var c in cmd.Commands) {
                if (c.Name != "QCF_x" && c.Name != "FF" && c.Name != "x") continue;
                var keys = CpuAI.KeysFor(c);
                var engine = new CommandEngine(cmd);
                bool done = false;
                foreach (var k in keys) { engine.Step(k); if (engine.Active(c.Name)) done = true; }
                Assert.IsTrue(done, c.Name + " completes from " + string.Join(" ", keys));
            }
        }

        [Test]
        public void CpuAI_BeatsAStandingDummy() {
            var e = NewFight(120f);
            e.TimerCount = -1;
            var ai = new CpuAI(e.P1, 8);
            int guard = 0;
            int life = e.P2.Life;
            while (e.P2.Life > 0 && guard++ < 60 * 60) e.Tick(ai.Tick(e.P1, e.P2, e), CmdKey.None);
            Assert.Less(e.P2.Life, life, "the CPU dealt damage");
            Assert.Greater(e.P1.HitCount + e.P1.UniqHitCount, 0);
        }

        [Test]
        public void CpuVsCpu_RoundEnds() {
            var e = NewFight(120f);
            var a = new CpuAI(e.P1, 6, 1);
            var b = new CpuAI(e.P2, 6, 2);
            int guard = 0;
            while (!e.RoundOver && guard++ < 99 * 60 + 200) e.Tick(a.Tick(e.P1, e.P2, e), b.Tick(e.P2, e.P1, e));
            Assert.IsTrue(e.RoundOver, "KO or time over");
            Assert.IsTrue(e.P1.Life < e.P1.LifeMax || e.P2.Life < e.P2.LifeMax, "they fought");
        }
    }
}
