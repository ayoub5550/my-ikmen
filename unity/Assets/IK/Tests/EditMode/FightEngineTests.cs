using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.3 gate: the fight engine in C# — expressions, .cmd commands, .cns states and the
    /// character state machine — checked against the real Kung Fu Man files in
    /// `assets/screenpack/chars/kfm`, with every expected number taken from those files or
    /// from the Go reference (`engine/ikemen-go/src/input.go`, `char.go`,
    /// `engine/ikemen-go/data/common1.cns.zss`). Never relax an expectation to make a test
    /// pass: check the source data first.
    /// </summary>
    public class FightEngineTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");

        static byte[] Read(string name) {
            var p = Path.Combine(Chars, name);
            Assert.IsTrue(File.Exists(p), "missing " + p);
            return File.ReadAllBytes(p);
        }

        static MugenCharacter character;
        static CnsFile cns;        // kfm.cns + the [Statedef -1] of kfm.cmd
        static CmdFile cmd;

        [OneTimeSetUp]
        public void LoadCharacter() {
            var source = new FileSource(Chars);
            character = MugenCharacter.Load(source, "kfm.def", loadSound: false);
            cmd = CmdFile.Parse(Read("kfm.cmd"));
            cns = CnsFile.Parse(Read("kfm.cns"));
            cns.Merge(cmd.States);
        }

        static Fighter NewFighter() => new Fighter(character, cns, cmd, () => 500f);

        /// <summary>Plays the same input for n logic ticks.</summary>
        static void Play(Fighter f, CmdKey keys, int ticks) {
            for (int i = 0; i < ticks; i++) { f.SetInput(keys); f.Tick(); }
        }

        /// <summary>A quarter-circle-forward roll ending on the given button.</summary>
        static void QuarterCircleForward(Fighter f, CmdKey button) {
            Play(f, CmdKey.D, 3);
            Play(f, CmdKey.D | CmdKey.F, 3);
            Play(f, CmdKey.F, 2);
            f.SetInput(CmdKey.F | button);
            f.Tick();
        }

        // =====================================================================
        //  Expressions (Expr) — the trigger language
        // =====================================================================

        class FakeContext : IExprContext {
            public readonly Dictionary<string, float> Values =
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public string LastStringArg;
            public float Rnd = 123f;
            public bool TryTrigger(string name, string arg, float argValue, out float value) {
                LastStringArg = arg;
                string key = arg != null ? name + ":" + arg : name;
                if (Values.TryGetValue(key, out value)) return true;
                return Values.TryGetValue(name, out value);
            }
            public float Random999() => Rnd;
            public FakeContext() { Values["random"] = 123f; }
        }

        static float Eval(string text, IExprContext ctx = null) => Expr.Parse(text).Eval(ctx);

        [Test] public void Arithmetic_follows_MUGEN_precedence() {
            Assert.AreEqual(14f, Eval("2 + 3 * 4"), 0.001f);
            Assert.AreEqual(20f, Eval("(2 + 3) * 4"), 0.001f);
            Assert.AreEqual(-6f, Eval("2 - 4 * 2"), 0.001f);
        }

        [Test] public void Power_is_right_associative() {
            Assert.AreEqual(512f, Eval("2 ** 3 ** 2"), 0.001f);
        }

        [Test] public void Division_by_zero_is_zero_not_an_exception() {
            Assert.AreEqual(0f, Eval("5 / 0"), 0.001f);
            Assert.AreEqual(0f, Eval("5 % 0"), 0.001f);
        }

        [Test] public void Logic_operators_return_one_or_zero() {
            Assert.AreEqual(1f, Eval("1 < 2 && 3 >= 3"), 0.001f);
            Assert.AreEqual(0f, Eval("0 || 0"), 0.001f);
            Assert.AreEqual(1f, Eval("1 ^^ 0"), 0.001f);
            Assert.AreEqual(1f, Eval("!0"), 0.001f);
        }

        [Test] public void Range_comparison_respects_open_and_closed_bounds() {
            Assert.AreEqual(1f, Eval("5 = [1,5]"), 0.001f, "5 is inside [1,5]");
            Assert.AreEqual(0f, Eval("5 = [1,5)"), 0.001f, "5 is outside [1,5)");
            Assert.AreEqual(1f, Eval("1 = [1,5)"), 0.001f);
            Assert.AreEqual(0f, Eval("1 = (1,5]"), 0.001f);
            Assert.AreEqual(1f, Eval("7 != [1,5]"), 0.001f, "outside a negated range is true");
        }

        [Test] public void Functions_match_the_MUGEN_set() {
            Assert.AreEqual(3f, Eval("abs(-3)"), 0.001f);
            Assert.AreEqual(2f, Eval("floor(2.7)"), 0.001f);
            Assert.AreEqual(3f, Eval("ceil(2.1)"), 0.001f);
            Assert.AreEqual(1f, Eval("min(1,2)"), 0.001f);
            Assert.AreEqual(2f, Eval("max(1,2)"), 0.001f);
            Assert.AreEqual(-1f, Eval("sign(-7)"), 0.001f);
            Assert.AreEqual(1f, Eval("fmod(7,3)"), 0.001f);
            Assert.AreEqual(10f, Eval("ifelse(1, 10, 20)"), 0.001f);
            Assert.AreEqual(20f, Eval("ifelse(0, 10, 20)"), 0.001f);
        }

        [Test] public void An_unknown_trigger_is_zero_and_is_reported() {
            var e = Expr.Parse("banana + 1");
            Assert.AreEqual(1f, e.Eval(new FakeContext()), 0.001f);
            CollectionAssert.Contains(e.UnknownNames, "banana");
        }

        [Test] public void String_triggers_pass_their_argument_through() {
            var ctx = new FakeContext();
            ctx.Values["command:QCF_x"] = 1f;
            Assert.AreEqual(1f, Eval("command = \"QCF_x\"", ctx), 0.001f);
            Assert.AreEqual(0f, Eval("command != \"QCF_x\"", ctx), 0.001f);
        }

        [Test] public void Flag_triggers_read_bare_letters() {
            var ctx = new FakeContext();
            ctx.Values["statetype:S"] = 1f;
            Assert.AreEqual(1f, Eval("statetype = S", ctx), 0.001f);
            Assert.AreEqual(1f, Eval("statetype != A", ctx), 0.001f, "statetype:A is unknown, so = A is 0 and != A is 1");
        }

        [Test] public void Two_word_triggers_are_one_name() {
            var ctx = new FakeContext();
            ctx.Values["vel x"] = 2.4f;
            ctx.Values["p2bodydist x"] = 12f;
            Assert.AreEqual(2.4f, Eval("vel x", ctx), 0.001f);
            Assert.AreEqual(1f, Eval("p2bodydist X < 20", ctx), 0.001f);
        }

        [Test] public void Random_comes_from_the_context_so_tests_are_deterministic() {
            var ctx = new FakeContext { Rnd = 777f };
            ctx.Values["random"] = 777f;
            Assert.AreEqual(777f, Eval("random", ctx), 0.001f);
            Assert.AreEqual(1f, Eval("random < 800", ctx), 0.001f);
            Assert.AreEqual(777f, Eval("random(0,999)", ctx), 0.001f, "the function form uses Random999");
        }

        [Test] public void AnimElem_is_a_comparison_on_AnimElemTime() {
            var ctx = new FakeContext();
            ctx.Values["animelemtime"] = 0f;
            Assert.AreEqual(1f, Eval("AnimElem = 3", ctx), 0.001f, "element starts this tick");
            ctx.Values["animelemtime"] = 4f;
            Assert.AreEqual(0f, Eval("AnimElem = 3", ctx), 0.001f, "4 ticks in is not the start");
            Assert.AreEqual(1f, Eval("AnimElem = 3, >= 0", ctx), 0.001f, "but it is at or past the start");
            Assert.AreEqual(1f, Eval("AnimElem = 3, 4", ctx), 0.001f, "explicit elapsed-time form");
        }

        // =====================================================================
        //  .cmd parsing
        // =====================================================================

        [Test] public void KFM_cmd_has_every_command_block() {
            Assert.AreEqual(37, cmd.Commands.Count, "kfm.cmd defines 37 [Command] blocks");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cmd.Commands) names.Add(c.Name);
            Assert.AreEqual(34, names.Count, "three names are defined twice (TripleKFPalm, SmashKFUpper, blocking)");
            Assert.AreEqual(2, cmd.All("TripleKFPalm").Count);
            Assert.AreEqual(15, cmd.DefaultTime);
            Assert.AreEqual(1, cmd.DefaultBufferTime);
        }

        [Test] public void Quarter_circle_command_compiles_to_four_steps() {
            var c = cmd.Get("QCF_x");
            Assert.AreEqual("~D, DF, F, x", c.Source);
            Assert.AreEqual(4, c.Steps.Count);
            Assert.AreEqual(CK.D, c.Steps[0].Keys[0].Key);
            Assert.IsTrue(c.Steps[0].Keys[0].Tilde, "~D is a release");
            Assert.AreEqual(CK.DF, c.Steps[1].Keys[0].Key, "diagonals are a single key");
            Assert.AreEqual(CK.F, c.Steps[2].Keys[0].Key);
            Assert.AreEqual(CK.x, c.Steps[3].Keys[0].Key);
            Assert.IsFalse(c.Steps[3].Keys[0].Tilde);
        }

        [Test] public void Repeated_direction_is_expanded_into_release_and_press() {
            var c = cmd.Get("FF");
            Assert.AreEqual("F, F", c.Source);
            Assert.AreEqual(3, c.Steps.Count, "F, F becomes F, >~F, >F");
            Assert.IsFalse(c.Steps[0].Greater);
            Assert.IsTrue(c.Steps[1].Keys[0].Tilde);
            Assert.IsTrue(c.Steps[1].Greater);
            Assert.IsTrue(c.Steps[2].Greater);
            Assert.IsFalse(c.Steps[2].Keys[0].Tilde);
        }

        [Test] public void Hold_and_loose_direction_symbols_are_parsed() {
            var c = cmd.Get("down_a");
            Assert.AreEqual("/$D,a", c.Source);
            Assert.AreEqual(2, c.Steps.Count);
            Assert.IsTrue(c.Steps[0].Keys[0].Slash, "/ means held");
            Assert.IsTrue(c.Steps[0].Keys[0].Dollar, "$ means 4-way");
            Assert.AreEqual(CK.D, c.Steps[0].Keys[0].Key);
            Assert.AreEqual(CK.a, c.Steps[1].Keys[0].Key);
            Assert.IsFalse(c.Steps[1].Keys[0].Slash);
        }

        [Test] public void Simultaneous_buttons_are_one_step_with_AND_logic() {
            var c = cmd.Get("recovery");
            Assert.AreEqual(1, c.Steps.Count);
            Assert.AreEqual(2, c.Steps[0].Keys.Count, "x+y is one step with two keys");
            Assert.IsFalse(c.Steps[0].OrLogic);
            var upper = cmd.Get("upper_xy");
            Assert.AreEqual(4, upper.Steps.Count, "~F, D, DF, x+y");
            Assert.AreEqual(2, upper.Steps[3].Keys.Count);
        }

        [Test] public void Charge_times_are_read_from_the_tilde_prefix() {
            var charge = new MugenCommand { Name = "charge_b" };
            CmdFile.ReadCommandSymbols(charge, "~30$B, F, x");
            charge.Prepare();
            Assert.AreEqual(30, charge.Steps[0].Keys[0].ChargeTime);
            Assert.IsTrue(charge.Steps[0].Keys[0].Tilde);
            Assert.IsTrue(charge.Steps[0].Keys[0].Dollar);
            Assert.AreEqual(0, charge.Steps[1].Keys[0].ChargeTime);
        }

        [Test] public void A_hold_only_command_always_buffers_for_one_tick() {
            var hold = new MugenCommand { Name = "holdfwd", MaxBufTime = 20 };
            CmdFile.ReadCommandSymbols(hold, "/$F");
            CmdFile.ApplyBackwardCompatibility(hold);
            Assert.AreEqual(1, hold.MaxBufTime, "Ikemen forces buffer.time = 1 for hold-only commands");
        }

        [Test] public void Command_times_come_from_the_file_not_the_defaults() {
            Assert.AreEqual(1, cmd.Get("down_a").MaxTime, "down_a sets time = 1");
            Assert.AreEqual(3, cmd.Get("blocking").MaxTime, "blocking sets time = 3");
            Assert.AreEqual(10, cmd.Get("FF").MaxTime, "FF sets time = 10");
            Assert.AreEqual(15, cmd.Get("QCF_x").MaxTime, "QCF_x uses the default of 15");
        }

        // =====================================================================
        //  Command matching (CommandEngine / InputBuffer)
        // =====================================================================

        static CommandEngine Engine() => new CommandEngine(cmd);
        static void Feed(CommandEngine e, CmdKey k, int ticks) {
            for (int i = 0; i < ticks; i++) e.Step(k);
        }

        [Test] public void A_quarter_circle_motion_fires_the_special() {
            var e = Engine();
            Feed(e, CmdKey.None, 5);
            Feed(e, CmdKey.D, 3);
            Feed(e, CmdKey.D | CmdKey.F, 3);
            Feed(e, CmdKey.F, 2);
            e.Step(CmdKey.F | CmdKey.x);
            Assert.IsTrue(e.Active("QCF_x"), "D, DF, F + x is QCF_x");
            CollectionAssert.Contains(e.JustCompleted, "QCF_x");
            e.Step(CmdKey.F);
            Assert.IsFalse(e.Active("QCF_x"), "buffer.time is 1 tick");
        }

        [Test] public void The_reverse_motion_does_not_fire_the_special() {
            var e = Engine();
            Feed(e, CmdKey.None, 5);
            Feed(e, CmdKey.F, 3);
            Feed(e, CmdKey.D | CmdKey.F, 3);
            Feed(e, CmdKey.D, 2);
            e.Step(CmdKey.D | CmdKey.x);
            Assert.IsFalse(e.Active("QCF_x"), "F, DF, D is a quarter circle back");
        }

        [Test] public void A_motion_slower_than_the_time_window_is_refused() {
            var e = Engine();
            Feed(e, CmdKey.None, 5);
            Feed(e, CmdKey.D, 3);
            Feed(e, CmdKey.D | CmdKey.F, 3);
            Feed(e, CmdKey.F, 20);              // way past time = 15
            e.Step(CmdKey.F | CmdKey.x);
            Assert.IsFalse(e.Active("QCF_x"));
        }

        [Test] public void A_double_tap_fires_but_a_single_tap_does_not() {
            var e = Engine();
            Feed(e, CmdKey.None, 3);
            Feed(e, CmdKey.F, 2);
            Assert.IsFalse(e.Active("FF"), "one tap is not a double tap");
            Feed(e, CmdKey.None, 2);
            e.Step(CmdKey.F);
            Assert.IsTrue(e.Active("FF"));
        }

        [Test] public void Hold_commands_follow_the_key_and_accept_diagonals() {
            var e = Engine();
            Feed(e, CmdKey.None, 3);
            Assert.IsFalse(e.Active("holddown"));
            e.Step(CmdKey.D);
            Assert.IsTrue(e.Active("holddown"), "/$D is true while down is held");
            e.Step(CmdKey.D | CmdKey.F);
            Assert.IsTrue(e.Active("holddown"), "$ matches the diagonal too");
            e.Step(CmdKey.None);
            Assert.IsFalse(e.Active("holddown"));
        }

        [Test] public void A_strict_direction_is_broken_by_a_diagonal_but_a_loose_one_is_not() {
            var strict = new MugenCommand { Name = "strict" };
            CmdFile.ReadCommandSymbols(strict, "D");
            strict.Prepare();
            var loose = new MugenCommand { Name = "loose" };
            CmdFile.ReadCommandSymbols(loose, "$D");
            loose.Prepare();
            var file = new CmdFile();
            file.Commands.Add(strict);
            file.Commands.Add(loose);
            var e = new CommandEngine(file);
            e.Step(CmdKey.None);
            e.Step(CmdKey.D | CmdKey.F);
            Assert.IsFalse(e.Active("strict"), "plain D is false while DF is held");
            Assert.IsTrue(e.Active("loose"), "$D is true while DF is held");
        }

        [Test] public void A_charge_command_needs_the_full_charge_time() {
            var charge = new MugenCommand { Name = "charge", MaxTime = 10 };
            CmdFile.ReadCommandSymbols(charge, "~30$B, x");
            charge.Prepare();
            var file = new CmdFile();
            file.Commands.Add(charge);

            var tooShort = new CommandEngine(file);
            Feed(tooShort, CmdKey.None, 5);
            Feed(tooShort, CmdKey.B, 10);
            tooShort.Step(CmdKey.None);
            tooShort.Step(CmdKey.x);
            Assert.IsFalse(tooShort.Active("charge"), "10 ticks of charge is not 30");

            charge.Clear(true);
            var longEnough = new CommandEngine(file);
            Feed(longEnough, CmdKey.None, 5);
            Feed(longEnough, CmdKey.B, 40);
            longEnough.Step(CmdKey.None);
            longEnough.Step(CmdKey.x);
            Assert.IsTrue(longEnough.Active("charge"), "40 ticks of charge is enough");
        }

        [Test] public void A_button_command_is_true_on_the_press_tick_only() {
            var e = Engine();
            Feed(e, CmdKey.None, 3);
            e.Step(CmdKey.x);
            Assert.IsTrue(e.Active("x"));
            e.Step(CmdKey.x);
            Assert.IsFalse(e.Active("x"), "holding x does not keep the command true");
        }

        [Test] public void The_input_buffer_counts_ticks_held_and_released() {
            var buf = new InputBuffer();
            buf.Step(CmdKey.None);
            buf.Step(CmdKey.F);
            Assert.AreEqual(1, buf.Fb, "1 = pressed this tick");
            buf.Step(CmdKey.F);
            Assert.AreEqual(2, buf.Fb);
            buf.Step(CmdKey.None);
            Assert.AreEqual(-1, buf.Fb, "-1 = released this tick");
            Assert.AreEqual(2, buf.Fp, "the previous value is kept for release checks");
        }

        // =====================================================================
        //  .cns parsing
        // =====================================================================

        [Test] public void KFM_cns_holds_every_state_plus_the_cmd_state() {
            Assert.AreEqual(59, cns.States.Count, "58 states in kfm.cns plus [Statedef -1] from kfm.cmd");
            Assert.IsNotNull(cns.Get(-1));
            Assert.IsNotNull(cns.Get(200), "stand light punch");
            Assert.IsNotNull(cns.Get(400), "crouch light punch");
            Assert.IsNotNull(cns.Get(1000), "light kung fu palm");
            Assert.IsNotNull(cns.Get(3000), "triple kung fu palm (super)");
        }

        [Test] public void Statedef_200_reads_its_parameters() {
            var s = cns.Get(200);
            Assert.AreEqual("S", s.Get("type"));
            Assert.AreEqual("A", s.Get("movetype"));
            Assert.AreEqual("S", s.Get("physics"));
            Assert.AreEqual("200", s.Get("anim"));
            Assert.AreEqual("10", s.Get("poweradd"));
            Assert.AreEqual("0", s.Get("ctrl"));
            Assert.AreEqual("0,0", s.Get("velset"));
            Assert.AreEqual("1", s.Get("juggle"));
        }

        [Test] public void Statedef_200_controllers_are_in_file_order() {
            var s = cns.Get(200);
            Assert.AreEqual(3, s.Controllers.Count);
            Assert.AreEqual("hitdef", s.Controllers[0].Type);
            Assert.AreEqual("23, 0", s.Controllers[0].Get("damage"));
            Assert.AreEqual("S, NA", s.Controllers[0].Get("attr"));
            Assert.AreEqual("playsnd", s.Controllers[1].Type);
            Assert.AreEqual("0, 0", s.Controllers[1].Get("value"));
            Assert.AreEqual("changestate", s.Controllers[2].Type);
            Assert.AreEqual("0", s.Controllers[2].Get("value"));
            Assert.AreEqual("1", s.Controllers[2].Get("ctrl"));
        }

        [Test] public void Trigger_groups_are_an_OR_of_ANDs() {
            var smash = cns.Get(-1).Controllers[0];      // [State -1, Smash Kung Fu Upper]
            Assert.AreEqual("changestate", smash.Type);
            Assert.AreEqual("3050", smash.Get("value"));
            Assert.AreEqual(3, smash.TriggerAll.Count, "command, power, statetype");
            Assert.AreEqual(3, smash.TriggerGroups.Count, "trigger1, trigger2, trigger3");
            Assert.AreEqual(1, smash.TriggerGroups[1].Count);
            Assert.AreEqual(3, smash.TriggerGroups[2].Count, "trigger2 is a three-line AND");
        }

        [Test] public void Trigger_names_are_collected_for_coverage() {
            var names = cns.TriggerNames();
            foreach (var expected in new[] { "command", "power", "statetype", "movecontact", "AnimElem", "Time" })
                Assert.IsTrue(names.Contains(expected), "expected trigger name " + expected);
        }

        [Test] public void A_controller_without_triggers_never_runs() {
            var sc = new StateController { Type = "null" };
            Assert.IsFalse(sc.TriggersPass(new FakeContext()), "no trigger lines means no run");
        }

        // =====================================================================
        //  The character state machine (Fighter + CommonStates)
        // =====================================================================

        [Test] public void A_fresh_character_stands_with_control() {
            var f = NewFighter();
            Assert.AreEqual(0, f.StateNo);
            Assert.AreEqual(0, f.AnimNo);
            Assert.IsTrue(f.Ctrl);
            Assert.AreEqual(StateType.Standing, f.Type);
            Assert.AreEqual(MoveType.Idle, f.Move);
            Assert.AreEqual(1000, f.Life, "kfm.cns [Data] life = 1000");
            Assert.AreEqual(2.4f, f.Const.WalkFwd, 0.001f);
            Assert.AreEqual(-8.4f, f.Const.JumpNeuY, 0.001f);
            Assert.AreEqual(0.44f, f.Const.YAccel, 0.001f);
        }

        [Test] public void Holding_forward_walks_at_the_characters_walk_speed() {
            var f = NewFighter();
            Play(f, CmdKey.F, 10);
            Assert.AreEqual(CommonStates.Walk, f.StateNo);
            Assert.AreEqual(20, f.AnimNo, "anim 20 is walk forward");
            Assert.AreEqual(24f, f.PosX, 0.001f, "10 ticks at walk.fwd = 2.4");
        }

        [Test] public void Holding_back_walks_backwards_with_its_own_animation() {
            var f = NewFighter();
            Play(f, CmdKey.B, 10);
            Assert.AreEqual(CommonStates.Walk, f.StateNo);
            Assert.AreEqual(21, f.AnimNo, "anim 21 is walk back");
            Assert.AreEqual(-22f, f.PosX, 0.001f, "10 ticks at walk.back = -2.2");
        }

        [Test] public void Releasing_the_direction_brakes_back_to_standing() {
            var f = NewFighter();
            Play(f, CmdKey.F, 6);
            Play(f, CmdKey.None, 6);
            Assert.AreEqual(CommonStates.Stand, f.StateNo);
            Assert.AreEqual(0f, f.VelX, 0.001f);
            Assert.AreEqual(0, f.AnimNo);
        }

        [Test] public void Down_crouches_through_the_transition_state_and_back() {
            var f = NewFighter();
            Play(f, CmdKey.D, 1);
            Assert.AreEqual(CommonStates.StandToCrouch, f.StateNo);
            Assert.AreEqual(10, f.AnimNo);
            Assert.AreEqual(StateType.Crouching, f.Type);
            Play(f, CmdKey.D, 10);
            Assert.AreEqual(CommonStates.Crouch, f.StateNo);
            Assert.AreEqual(11, f.AnimNo);
            Assert.IsTrue(f.Ctrl, "crouching keeps control");
            Play(f, CmdKey.None, 1);
            Assert.AreEqual(CommonStates.CrouchToStand, f.StateNo);
            Play(f, CmdKey.None, 10);
            Assert.AreEqual(CommonStates.Stand, f.StateNo);
        }

        [Test] public void Up_jumps_after_the_three_tick_jump_start() {
            var f = NewFighter();
            Play(f, CmdKey.U, 1);
            Assert.AreEqual(CommonStates.JumpStart, f.StateNo);
            Assert.AreEqual(40, f.AnimNo);
            Assert.IsFalse(f.Ctrl, "no control during the jump start");
            Play(f, CmdKey.U, 3);
            Assert.AreEqual(CommonStates.JumpUp, f.StateNo);
            Assert.AreEqual(41, f.AnimNo, "neutral jump uses anim 41");
            Assert.IsTrue(f.Ctrl);
            Assert.AreEqual(-8.4f + 0.44f, f.VelY, 0.01f, "jump.neu y plus one tick of gravity");
        }

        [Test] public void A_jump_rises_about_eighty_units_and_lands_on_the_ground() {
            var f = NewFighter();
            float apex = 0f;
            int landed = -1;
            for (int i = 0; i < 120; i++) {
                f.SetInput(i < 4 ? CmdKey.U : CmdKey.None);
                f.Tick();
                apex = Math.Min(apex, f.PosY);
                if (f.StateNo == CommonStates.JumpLand && landed < 0) landed = i;
            }
            Assert.Less(apex, -78f, "8.4 / 0.44 gives roughly 84 units of height");
            Assert.Greater(apex, -90f);
            Assert.GreaterOrEqual(landed, 20, "the whole jump takes about 40 ticks");
            Assert.AreEqual(CommonStates.Stand, f.StateNo, "and it ends standing");
            Assert.AreEqual(0f, f.PosY, 0.001f, "exactly on the ground");
            Assert.IsTrue(f.Ctrl);
        }

        [Test] public void A_forward_jump_carries_the_forward_jump_velocity() {
            var f = NewFighter();
            Play(f, CmdKey.U | CmdKey.F, 6);
            Assert.AreEqual(CommonStates.JumpUp, f.StateNo);
            Assert.AreEqual(2.5f, f.VelX, 0.001f, "jump.fwd = 2.5");
            Assert.AreEqual(42, f.AnimNo, "anim 42 is the forward jump");
        }

        [Test] public void One_air_jump_is_allowed_and_only_above_the_minimum_height() {
            var f = NewFighter();
            Play(f, CmdKey.U, 6);
            Play(f, CmdKey.None, 10);
            Assert.AreEqual(CommonStates.JumpUp, f.StateNo);
            Assert.Less(f.PosY, -f.Const.AirJumpHeight, "high enough to air jump");
            Play(f, CmdKey.U, 1);
            Assert.AreEqual(CommonStates.AirJumpStart, f.StateNo);
            Assert.AreEqual(1, f.AirJumpCount);
            Play(f, CmdKey.U, 3);
            Assert.AreEqual(CommonStates.JumpUp, f.StateNo);
            Assert.AreEqual(-8.1f + 0.44f * 2f, f.VelY, 0.02f, "airjump.neu y = -8.1");
            Play(f, CmdKey.None, 3);
            Play(f, CmdKey.U, 2);
            Assert.AreEqual(1, f.AirJumpCount, "airjump.num = 1, so there is no second air jump");
        }

        [Test] public void Double_tapping_forward_runs_and_stops_on_release() {
            var f = NewFighter();
            Play(f, CmdKey.None, 3);
            Play(f, CmdKey.F, 2);
            Play(f, CmdKey.None, 2);
            Play(f, CmdKey.F, 4);
            Assert.AreEqual(CommonStates.RunFwd, f.StateNo);
            Assert.AreEqual(100, f.AnimNo);
            // the state sets vel x every tick and friction is applied after the move, so the
            // honest measure of run speed is how far the character travels in one tick
            float before = f.PosX;
            Play(f, CmdKey.F, 1);
            Assert.AreEqual(4.6f, f.PosX - before, 0.001f, "run.fwd = 4.6 units per tick");
            Play(f, CmdKey.None, 1);
            Assert.AreEqual(CommonStates.Stand, f.StateNo);
        }

        [Test] public void Double_tapping_back_hops_backwards_and_recovers() {
            var f = NewFighter();
            Play(f, CmdKey.None, 3);
            Play(f, CmdKey.B, 2);
            Play(f, CmdKey.None, 2);
            Play(f, CmdKey.B, 1);
            Assert.AreEqual(CommonStates.HopBack, f.StateNo);
            Assert.AreEqual(-4.5f, f.VelX, 0.001f, "run.back x = -4.5");
            Assert.AreEqual(105, f.AnimNo);
            int ticks = 0;
            while (f.StateNo != CommonStates.Stand && ticks < 60) { Play(f, CmdKey.None, 1); ticks++; }
            Assert.Less(ticks, 40, "the hop recovers in well under a second");
            Assert.AreEqual(0f, f.PosY, 0.001f);
        }

        [Test] public void The_light_punch_runs_its_whole_state() {
            var f = NewFighter();
            f.SetInput(CmdKey.x);
            f.Tick();
            Assert.AreEqual(200, f.StateNo, "[State -1, Stand Light Punch]");
            Assert.AreEqual(200, f.AnimNo);
            Assert.AreEqual(MoveType.Attack, f.Move);
            Assert.IsFalse(f.Ctrl, "ctrl = 0 in statedef 200");
            Assert.AreEqual(10, f.Power, "poweradd = 10");
            Assert.IsNull(f.LastSound, "PlaySnd waits for Time = 1");

            int hitTick = -1, endTick = -1;
            for (int i = 0; i < 40; i++) {
                f.SetInput(CmdKey.None);
                f.Tick();
                if (f.HitDefCount > 0 && hitTick < 0) hitTick = i;
                if (f.StateNo == 0 && endTick < 0) endTick = i;
            }
            Assert.AreEqual("0, 0", f.LastSound, "PlaySnd 0,0 fired on Time = 1");
            Assert.AreEqual(1, f.HitDefCount, "the HitDef of AnimElem = 3 fires exactly once");
            Assert.AreEqual("S, NA dmg=23, 0", f.LastHitDef);
            Assert.GreaterOrEqual(hitTick, 1);
            Assert.Less(hitTick, 8, "the hit comes out in the first third of the animation");
            Assert.Greater(endTick, hitTick);
            Assert.IsTrue(f.Ctrl, "control returns with ChangeState ctrl = 1");
        }

        [Test] public void Crouching_plus_punch_uses_the_crouching_attack() {
            var f = NewFighter();
            Play(f, CmdKey.D, 12);
            f.SetInput(CmdKey.D | CmdKey.x);
            f.Tick();
            Assert.AreEqual(400, f.StateNo, "[State -1, Crouching Light Punch]");
            Assert.AreEqual(400, f.AnimNo);
            Assert.AreEqual(MoveType.Attack, f.Move);
        }

        [Test] public void Start_taunts() {
            var f = NewFighter();
            f.SetInput(CmdKey.s);
            f.Tick();
            Assert.AreEqual(195, f.StateNo);
            Assert.AreEqual(195, f.AnimNo);
        }

        [Test] public void A_quarter_circle_forward_and_punch_is_the_kung_fu_palm() {
            var f = NewFighter();
            QuarterCircleForward(f, CmdKey.x);
            Assert.AreEqual(1000, f.StateNo, "[State -1, Light Kung Fu Palm]");
            Assert.AreEqual(1000, f.AnimNo);
            Assert.AreEqual(1, f.Vars[1], "the combo condition var(1) is set by state -1");
        }

        [Test] public void The_super_needs_its_power_and_its_double_motion() {
            var f = NewFighter();
            f.Power = 3000;
            Play(f, CmdKey.None, 3);
            Play(f, CmdKey.D, 2);
            Play(f, CmdKey.D | CmdKey.F, 2);
            Play(f, CmdKey.F, 2);
            Play(f, CmdKey.D, 2);
            Play(f, CmdKey.D | CmdKey.F, 2);
            Play(f, CmdKey.F, 1);
            f.SetInput(CmdKey.F | CmdKey.x);
            f.Tick();
            Assert.AreEqual(3000, f.StateNo, "[State -1, Triple Kung Fu Palm]");

            var poor = NewFighter();
            poor.Power = 0;
            Play(poor, CmdKey.None, 3);
            Play(poor, CmdKey.D, 2);
            Play(poor, CmdKey.D | CmdKey.F, 2);
            Play(poor, CmdKey.F, 2);
            Play(poor, CmdKey.D, 2);
            Play(poor, CmdKey.D | CmdKey.F, 2);
            Play(poor, CmdKey.F, 1);
            poor.SetInput(CmdKey.F | CmdKey.x);
            poor.Tick();
            Assert.AreNotEqual(3000, poor.StateNo, "power >= 1000 is required");
        }

        [Test] public void Running_cannot_be_interrupted_by_the_hardcoded_walk() {
            var f = NewFighter();
            Play(f, CmdKey.None, 3);
            Play(f, CmdKey.F, 2);
            Play(f, CmdKey.None, 2);
            Play(f, CmdKey.F, 10);
            Assert.AreEqual(CommonStates.RunFwd, f.StateNo, "AssertSpecial noWalk keeps the run");
        }

        [Test] public void Twenty_thousand_random_ticks_leave_the_engine_sane() {
            var rng = new System.Random(20260104);
            var f = new Fighter(character, cns, cmd, () => rng.Next(0, 1000));
            var keys = new[] {
                CmdKey.None, CmdKey.F, CmdKey.B, CmdKey.D, CmdKey.U, CmdKey.D | CmdKey.F, CmdKey.D | CmdKey.B,
                CmdKey.x, CmdKey.y, CmdKey.a, CmdKey.b, CmdKey.c, CmdKey.z, CmdKey.s,
                CmdKey.D | CmdKey.x, CmdKey.F | CmdKey.y, CmdKey.x | CmdKey.y
            };
            var visited = new HashSet<int>();
            var current = CmdKey.None;
            for (int i = 0; i < 20000; i++) {
                if (rng.Next(0, 4) == 0) current = keys[rng.Next(keys.Length)];
                f.Power = Math.Min(3000, f.Power + 1);
                f.SetInput(current);
                f.Tick();
                visited.Add(f.StateNo);
                Assert.IsFalse(float.IsNaN(f.PosX) || float.IsNaN(f.PosY), "position stayed a number at tick " + i);
            }
            Assert.Greater(visited.Count, 20, "random play reaches many of the character's states");
            CollectionAssert.IsEmpty(f.UnknownTriggers, "every trigger KFM uses is implemented");
            CollectionAssert.IsEmpty(f.UnknownControllers, "every state controller KFM uses is recognised");
            Assert.Greater(f.HitDefCount, 100, "attacks kept coming out");
        }

        [Test] public void An_unknown_trigger_in_a_state_does_not_break_the_character() {
            var text = "[Statedef 7000]\ntype = S\n\n[State 7000, 1]\ntype = ChangeState\ntrigger1 = banana\nvalue = 0\n";
            var custom = CnsFile.Parse(text);
            var f = new Fighter(character, custom, cmd, () => 0f);
            f.SetInput(CmdKey.None);
            f.Tick();
            Assert.AreEqual(0, f.StateNo, "the state machine kept running");
        }
    }
}
