using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.4 gate: the hit system and the round flow, Kung Fu Man against Kung Fu Man.
    ///
    /// Every expected number is taken from the character's own files
    /// (`assets/screenpack/chars/kfm/kfm.cns`, `kfm.cmd`) or from the Go reference
    /// (`engine/ikemen-go/src/char.go`, `engine/ikemen-go/data/common1.cns.zss`). The quoted
    /// line is named in a comment next to each expectation. Never relax an expectation to make
    /// a test pass — read the source data first.
    /// </summary>
    public class HitSystemTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");
        static string Stages => Path.Combine(Repo, "assets", "screenpack", "stages");
        static string Data => Path.Combine(Repo, "assets", "screenpack", "data");

        static MugenCharacter character;
        static CnsFile cns;
        static CmdFile cmd;

        [OneTimeSetUp]
        public void LoadCharacter() {
            var source = new FileSource(Chars);
            character = MugenCharacter.Load(source, "kfm.def", loadSound: false);
            cmd = CmdFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cmd")));
            cns = CnsFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cns")));
            cns.Merge(cmd.States);
        }

        static Fighter NewFighter() => new Fighter(character, cns, cmd, () => 500f);

        /// <summary>A fight with both players at a chosen distance, already in the fighting phase.</summary>
        static FightEngine NewFight(float distance = 40f, StageDefinition stage = null) {
            var f = new FightEngine(NewFighter(), NewFighter(), stage);
            f.AnnounceTime = 0;
            f.Tick(CmdKey.None, CmdKey.None);          // leaves the announcement phase
            f.P1.PosX = -distance / 2f;
            f.P2.PosX = distance / 2f;
            f.P1.Facing = 1;
            f.P2.Facing = -1;
            return f;
        }

        static void Run(FightEngine e, CmdKey p1, CmdKey p2, int ticks) {
            for (int i = 0; i < ticks; i++) e.Tick(p1, p2);
        }

        /// <summary>P1 punches (kfm.cmd command "x") and the fight runs until the HitDef of
        /// state 200 connects — it fires on `AnimElem = 3`, a few ticks after the press.
        /// Returns the tick the contact happened on, or -1.</summary>
        static int Punch(FightEngine e, CmdKey p2Hold = CmdKey.None, int maxTicks = 25) {
            e.Tick(CmdKey.x, p2Hold);
            if (e.P1.MoveContactFlag != 0) return 0;
            for (int i = 1; i < maxTicks; i++) {
                e.Tick(CmdKey.None, p2Hold);
                if (e.P1.MoveContactFlag != 0) return i;
            }
            return -1;
        }

        // =====================================================================
        //  HitDef parsing
        // =====================================================================

        [Test]
        public void HitDef_of_state_200_is_read_with_the_characters_own_numbers() {
            // kfm.cns [State 200, 1]: attr = S, NA ; damage = 23, 0 ; animtype = Light
            // guardflag = MA ; hitflag = MAF ; priority = 3, Hit ; pausetime = 8, 8
            // sparkno = 0 ; sparkxy = -10, -76 ; hitsound = 5, 0 ; guardsound = 6, 0
            // ground.type = High ; ground.slidetime = 5 ; ground.hittime = 11
            // ground.velocity = -4 ; airguard.velocity = -1.9,-.8 ; air.velocity = -1.4,-3
            // air.hittime = 15
            var f = NewFighter();
            var state = cns.Get(200);
            Assert.IsNotNull(state, "statedef 200 missing");
            StateController hitdef = null;
            foreach (var c in state.Controllers) if (c.Type == "hitdef") { hitdef = c; break; }
            Assert.IsNotNull(hitdef, "state 200 has no HitDef");

            var hd = HitDef.Read(hitdef, f.EvalFloat, f.Const);
            Assert.AreEqual(HitAttr.StandAttack | HitAttr.Normal | HitAttr.Attack, hd.Attr);
            Assert.AreEqual(23, hd.HitDamage);
            Assert.AreEqual(0, hd.GuardDamage);
            Assert.AreEqual(Reaction.Light, hd.AnimType);
            Assert.AreEqual(8, hd.PauseTime[0]);
            Assert.AreEqual(8, hd.PauseTime[1]);
            Assert.AreEqual(HitKind.High, hd.GroundType);
            Assert.AreEqual(5, hd.GroundSlideTime);
            Assert.AreEqual(11, hd.GroundHitTime);
            Assert.AreEqual(15, hd.AirHitTime);
            Assert.AreEqual(3, hd.Priority);
            Assert.AreEqual(HitFlag.High | HitFlag.Low | HitFlag.Air | HitFlag.Fall, hd.Flag);
            Assert.AreEqual(GuardFlag.High | GuardFlag.Low | GuardFlag.Air, hd.Guard);
            Assert.AreEqual(-4f, hd.GroundVelocity[0], 1e-4f);
            Assert.AreEqual(-1.4f, hd.AirVelocity[0], 1e-4f);
            Assert.AreEqual(-3f, hd.AirVelocity[1], 1e-4f);
            Assert.AreEqual(0, hd.Unsupported.Count, "unsupported HitDef keys: " + string.Join(",", hd.Unsupported));
        }

        [Test]
        public void HitDef_defaults_follow_the_go_reference() {
            // char.go HitDef.reset: hitflag H+L+A+F, priority 4 / Hit, air.hittime 20,
            // yaccel .35, fall.yvelocity -4.5, numhits 1, affectteam enemy.
            var c = new StateController { Type = "hitdef" };
            c.Params["attr"] = "S, NA";
            var hd = HitDef.Read(c, s => 0f, new CharConstants());
            Assert.AreEqual(HitFlag.High | HitFlag.Low | HitFlag.Air | HitFlag.Fall, hd.Flag);
            Assert.AreEqual(4, hd.Priority);
            Assert.AreEqual(TradeType.Hit, hd.PriorityType);
            Assert.AreEqual(20, hd.AirHitTime);
            Assert.AreEqual(0.35f, hd.YAccel, 1e-5f);
            Assert.AreEqual(-4.5f, hd.FallYVelocity, 1e-5f);
            Assert.AreEqual(1, hd.NumHits);
            Assert.AreEqual(1, hd.AffectTeam);
            Assert.AreEqual(GuardFlag.None, hd.Guard);
        }

        [Test]
        public void HitDef_guard_values_default_to_the_hit_values() {
            // char.go finalizeParams: guard.pausetime ← pausetime, guard.hittime ← ground.hittime,
            // guard.slidetime ← guard.hittime, guard.ctrltime ← guard.hittime, air.type ← ground.type
            var c = new StateController { Type = "hitdef" };
            c.Params["attr"] = "S, NA";
            c.Params["pausetime"] = "8, 9";
            c.Params["ground.hittime"] = "14";
            c.Params["ground.type"] = "Low";
            var hd = HitDef.Read(c, s => MugenDef.Atof(s), new CharConstants());
            Assert.AreEqual(8, hd.GuardPauseTime[0]);
            Assert.AreEqual(9, hd.GuardPauseTime[1]);
            Assert.AreEqual(14, hd.GuardHitTime);
            Assert.AreEqual(14, hd.GuardSlideTime);
            Assert.AreEqual(14, hd.GuardCtrlTime);
            Assert.AreEqual(HitKind.Low, hd.AirType);
        }

        [Test]
        public void Attr_parsing_matches_the_mugen_codes() {
            var a = HitDef.ParseAttr("S, NA");
            Assert.IsTrue((a & HitAttr.StandAttack) != 0);
            Assert.IsTrue((a & HitAttr.Normal) != 0);
            Assert.IsTrue((a & HitAttr.Attack) != 0);
            var b = HitDef.ParseAttr("SCA, HP");
            Assert.AreEqual(HitAttr.StateMask, b & HitAttr.StateMask);
            Assert.IsTrue((b & HitAttr.Hyper) != 0);
            Assert.IsTrue((b & HitAttr.Projectile) != 0);
            var t = HitDef.ParseAttr("S, NT");
            Assert.IsTrue((t & HitAttr.Throw) != 0);
        }

        [Test]
        public void HitFlag_and_guardflag_letters_are_read() {
            Assert.AreEqual(HitFlag.High | HitFlag.Low, HitDef.ParseHitFlag("M"));
            Assert.AreEqual(HitFlag.High | HitFlag.Low | HitFlag.Air, HitDef.ParseHitFlag("MA"));
            Assert.IsTrue((HitDef.ParseHitFlag("MAF") & HitFlag.Fall) != 0);
            Assert.AreEqual(GuardFlag.High | GuardFlag.Low, HitDef.ParseGuardFlag("M"));
            Assert.AreEqual(GuardFlag.Low, HitDef.ParseGuardFlag("L"));
        }

        // =====================================================================
        //  Collision and damage
        // =====================================================================

        [Test]
        public void A_standing_punch_connects_and_takes_exactly_its_damage() {
            // kfm.cns [State 200, 1] damage = 23, 0 ; both characters have attack = defence = 100
            var e = NewFight(35f);
            int before = e.P2.Life;
            Assert.GreaterOrEqual(Punch(e), 0, "the punch never connected");
            Assert.AreEqual(1, e.P1.HitCount, "the punch did not connect");
            Assert.AreEqual(before - 23, e.P2.Life);
            Assert.AreEqual(1, e.P1.MoveHitFlag);
            Assert.AreEqual(0, e.P1.MoveGuardedFlag);
        }

        [Test]
        public void A_punch_out_of_range_does_not_connect() {
            var e = NewFight(300f);
            Run(e, CmdKey.x, CmdKey.None, 25);
            Assert.AreEqual(0, e.P1.HitCount);
            Assert.AreEqual(e.P2.LifeMax, e.P2.Life);
        }

        [Test]
        public void The_receiver_enters_the_hit_states_and_recovers() {
            var e = NewFight(35f);
            Assert.GreaterOrEqual(Punch(e), 0, "the punch never connected");
            // common1.cns.zss 5000: stand get-hit (shaking) while hitshaketime runs
            Assert.AreEqual(HitStates.StandShake, e.P2.StateNo);
            Assert.AreEqual(MoveType.BeingHit, e.P2.Move);
            // pausetime = 8, 8 → the shake lasts 8 ticks, then state 5001 (knocked back)
            Run(e, CmdKey.None, CmdKey.None, 8);
            Assert.AreEqual(HitStates.StandKnock, e.P2.StateNo);
            // ground.hittime = 14 → control comes back and the character stands again
            Run(e, CmdKey.None, CmdKey.None, 20);
            Assert.AreEqual(0, e.P2.StateNo);
            Assert.IsTrue(e.P2.Ctrl);
            Assert.AreEqual(MoveType.Idle, e.P2.Move);
        }

        [Test]
        public void The_attacker_is_frozen_for_its_own_pausetime() {
            // pausetime = 8, 8 → the attacker also freezes for 8 ticks
            var e = NewFight(35f);
            Assert.GreaterOrEqual(Punch(e), 0, "the punch never connected");
            Assert.IsTrue(e.P1.HitPauseTime > 0, "pausetime = 8, 8 freezes the attacker too");
            int state = e.P1.StateNo, time = e.P1.StateTime;
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(state, e.P1.StateNo);
            Assert.AreEqual(time, e.P1.StateTime, "state time advanced during hit pause");
        }

        [Test]
        public void Holding_back_blocks_the_punch_and_costs_no_life() {
            // kfm's light punch has guardflag = MA, so holding back blocks it
            var e = NewFight(35f);
            int before = e.P2.Life;
            // B is already facing-relative, so it is "back" for both players
            Run(e, CmdKey.None, CmdKey.B, 2);      // start holding back first
            Assert.GreaterOrEqual(Punch(e, CmdKey.B), 0, "the punch never reached the guard");
            Assert.AreEqual(1, e.P1.MoveGuardedFlag, "the attack was not guarded");
            Assert.AreEqual(0, e.P1.MoveHitFlag);
            Assert.AreEqual(before, e.P2.Life, "guard damage of this HitDef is 0");
            Assert.IsTrue(e.P2.StateNo == HitStates.StandGuardShake ||
                          e.P2.StateNo == HitStates.StandGuardKnock ||
                          e.P2.StateNo == HitStates.StandGuard,
                          "unexpected guard state " + e.P2.StateNo);
        }

        [Test]
        public void A_hitdef_connects_only_once_per_attack() {
            var e = NewFight(35f);
            Punch(e);
            Run(e, CmdKey.None, CmdKey.None, 25);
            Assert.AreEqual(1, e.P1.HitCount, "the same HitDef hit more than once");
        }

        [Test]
        public void Damage_scales_with_attack_and_defence() {
            var f = NewFighter();
            var attacker = NewFighter();
            // char.go computeDamage: damage × attack% ÷ defence%
            attacker.AttackMul = 2f;
            Assert.AreEqual(46, f.ComputeDamage(23, attacker));
            attacker.AttackMul = 1f;
            f.DefenceMul = 2f;
            Assert.AreEqual(11, f.ComputeDamage(23, attacker));
        }

        [Test]
        public void Life_never_goes_below_zero_and_sets_the_ko_flag() {
            var e = NewFight(35f);
            e.P2.Life = 5;
            Assert.GreaterOrEqual(Punch(e), 0, "the punch never connected");
            Assert.AreEqual(0, e.P2.Life);
            Assert.IsTrue(e.P2.KO);
        }

        // =====================================================================
        //  Round flow
        // =====================================================================

        [Test]
        public void The_round_starts_with_an_announcement_then_gives_control() {
            var e = new FightEngine(NewFighter(), NewFighter());
            e.AnnounceTime = 30;
            Assert.AreEqual(RoundState.Announce, e.State);
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.IsFalse(e.P1.Ctrl, "players must not move during the announcement");
            for (int i = 0; i < 30; i++) e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(RoundState.Fighting, e.State);
            Assert.IsTrue(e.P1.Ctrl);
        }

        [Test]
        public void A_ko_ends_the_round_and_counts_a_win() {
            var e = NewFight(35f);
            e.OverTime = 2;
            e.P2.Life = 5;
            Assert.GreaterOrEqual(Punch(e), 0, "the punch never connected");
            Assert.AreEqual(RoundState.Over, e.State);
            Assert.AreEqual(1, e.RoundWinner);
            Run(e, CmdKey.None, CmdKey.None, 3);
            Assert.AreEqual(RoundState.WinPose, e.State);
            Assert.AreEqual(1, e.Wins[0]);
            Assert.AreEqual(0, e.Wins[1]);
        }

        [Test]
        public void Two_round_wins_end_the_match() {
            var e = NewFight(35f);
            e.OverTime = 1;
            e.WinPoseTime = 1;
            for (int round = 0; round < 2; round++) {
                e.P2.Life = 5;
                e.State = RoundState.Fighting;
                e.P1.PosX = -17.5f; e.P2.PosX = 17.5f;
                Assert.GreaterOrEqual(Punch(e), 0, "round " + round + ": the punch never connected");
                Run(e, CmdKey.None, CmdKey.None, 8);
            }
            Assert.AreEqual(2, e.Wins[0]);
            Assert.IsTrue(e.MatchOver);
        }

        [Test]
        public void The_timer_counts_down_at_the_configured_rate_and_decides_on_life() {
            var e = NewFight(200f);
            e.FramesPerCount = 2;      // fight.def uses 60; shortened so the test stays fast
            e.TimerCount = 2;
            e.StartRound(1);
            e.AnnounceTime = 0;
            e.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(RoundState.Fighting, e.State);
            e.P1.Life = 500;
            e.P2.Life = 400;
            Run(e, CmdKey.None, CmdKey.None, 10);
            Assert.AreEqual(RoundState.Over, e.State);
            Assert.AreEqual(1, e.RoundWinner, "the fighter with more life wins on time over");
        }

        // =====================================================================
        //  Positioning: pushing, bounds, facing
        // =====================================================================

        [Test]
        public void Push_boxes_keep_the_fighters_apart() {
            // kfm.cns [Size] ground.front = 16, ground.back = 15 → the boxes are 31 wide
            var e = NewFight(4f);
            Run(e, CmdKey.None, CmdKey.None, 2);
            float gap = Math.Abs(e.P2.PosX - e.P1.PosX);
            Assert.GreaterOrEqual(gap, 20f, "the fighters are standing inside each other");
        }

        [Test]
        public void A_grounded_fighter_with_control_turns_to_face_the_opponent() {
            var e = NewFight(60f);
            e.P1.PosX = 100f;          // P1 is now to the right of P2
            e.P2.PosX = 0f;
            Run(e, CmdKey.None, CmdKey.None, 2);
            Assert.AreEqual(-1, e.P1.Facing);
            Assert.AreEqual(1, e.P2.Facing);
        }

        [Test]
        public void Fighters_stay_inside_the_stage_bounds() {
            var stage = StageDefinition.Load(new FileSource(Stages), "kfm.def", false);
            var e = new FightEngine(NewFighter(), NewFighter(), stage);
            e.AnnounceTime = 0;
            e.Tick(CmdKey.None, CmdKey.None);
            e.P1.PosX = -5000f;
            e.P2.PosX = 5000f;
            e.Tick(CmdKey.None, CmdKey.None);
            // kfm.def [PlayerInfo] leftbound = -1000, rightbound = 1000
            Assert.GreaterOrEqual(e.P1.PosX, (float)stage.LeftBound);
            Assert.LessOrEqual(e.P2.PosX, (float)stage.RightBound);
        }

        // =====================================================================
        //  Opponent-aware triggers
        // =====================================================================

        [Test]
        public void Opponent_triggers_report_the_real_distance_and_state() {
            var e = NewFight(100f);
            Run(e, CmdKey.None, CmdKey.None, 2);
            float v;
            Assert.IsTrue(e.P1.TryTrigger("p2dist x", null, 0f, out v));
            Assert.AreEqual(e.P2.PosX - e.P1.PosX, v, 0.5f);
            Assert.IsTrue(e.P1.TryTrigger("p2statetype", "S", 0f, out v));
            Assert.AreEqual(1f, v);
            Assert.IsTrue(e.P1.TryTrigger("numenemy", null, 0f, out v));
            Assert.AreEqual(1f, v);
            Assert.IsTrue(e.P1.TryTrigger("roundstate", null, 0f, out v));
            Assert.AreEqual(2f, v, "roundstate must be 2 while fighting");
        }

        [Test]
        public void Movecontact_and_gethitvar_are_live_after_a_hit() {
            var e = NewFight(35f);
            Assert.GreaterOrEqual(Punch(e), 0, "the punch never connected");
            float v;
            Assert.IsTrue(e.P1.TryTrigger("movecontact", null, 0f, out v));
            Assert.AreEqual(1f, v);
            Assert.IsTrue(e.P2.TryTrigger("gethitvar", "damage", 0f, out v));
            Assert.AreEqual(23f, v);
            Assert.IsTrue(e.P2.TryTrigger("gethitvar", "hitshaketime", 0f, out v));
            Assert.Greater(v, 0f);
            Assert.IsTrue(e.P1.TryTrigger("numtarget", null, 0f, out v));
            Assert.AreEqual(1f, v);
        }

        [Test]
        public void Hitdefattr_is_true_only_while_the_hitdef_is_live() {
            var e = NewFight(300f);
            float v;
            Assert.IsTrue(e.P1.TryTrigger("hitdefattr", "SCA, NA", 0f, out v));
            Assert.AreEqual(0f, v, "no HitDef has been set yet");
            // the HitDef of state 200 is set on AnimElem = 3
            for (int i = 0; i < 10 && !e.P1.HitDefActive; i++) e.Tick(i == 0 ? CmdKey.x : CmdKey.None, CmdKey.None);
            Assert.IsTrue(e.P1.TryTrigger("hitdefattr", "SCA, NA", 0f, out v));
            Assert.AreEqual(1f, v, "state 200's HitDef is S, NA");
        }

        // =====================================================================
        //  Soak
        // =====================================================================

        [Test]
        public void Ten_thousand_ticks_of_random_input_never_break_the_fight() {
            var e = NewFight(60f);
            var rng = new System.Random(4242);
            var keys = new[] { CmdKey.None, CmdKey.F, CmdKey.B, CmdKey.U, CmdKey.D,
                               CmdKey.a, CmdKey.b, CmdKey.c, CmdKey.x, CmdKey.y, CmdKey.z };
            for (int i = 0; i < 10000; i++) {
                var k1 = keys[rng.Next(keys.Length)];
                var k2 = keys[rng.Next(keys.Length)];
                e.Tick(k1, k2);
                Assert.IsFalse(float.IsNaN(e.P1.PosX) || float.IsNaN(e.P2.PosX), "NaN position at tick " + i);
                Assert.IsFalse(float.IsNaN(e.P1.VelY) || float.IsNaN(e.P2.VelY), "NaN velocity at tick " + i);
                Assert.GreaterOrEqual(e.P1.Life, 0);
                Assert.GreaterOrEqual(e.P2.Life, 0);
            }
            Assert.AreEqual(0, e.P1.UnknownControllers.Count,
                "unimplemented controllers: " + string.Join(",", e.P1.UnknownControllers));
        }
    }
}
