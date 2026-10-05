using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IK.Input;
using IK.Settings;

namespace IK.Tests {
    /// <summary>
    /// docs/TOUCH_AND_SETTINGS.md §6, EditMode cases 1-5: quantisation, SOCD, button
    /// assist, tick latching and the command recogniser on synthetic touch traces.
    /// </summary>
    public class InputLogicTests {
        // ---- 1. D-pad quantisation -------------------------------------------------
        [Test]
        public void Quantise_360_degrees_maps_to_the_expected_8_directions() {
            for (int deg = 0; deg < 360; deg++) {
                var v = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                bool U, D, L, R;
                InputLogic.Quantise8(v, 0.25f, out U, out D, out L, out R);
                int sector = InputLogic.SectorIndex(deg);
                Assert.AreEqual(sector == 0 || sector == 1 || sector == 7, R, "R at " + deg);
                Assert.AreEqual(sector == 3 || sector == 4 || sector == 5, L, "L at " + deg);
                Assert.AreEqual(sector == 1 || sector == 2 || sector == 3, U, "U at " + deg);
                Assert.AreEqual(sector == 5 || sector == 6 || sector == 7, D, "D at " + deg);
                Assert.IsFalse(U && D, "never U and D at " + deg);
                Assert.IsFalse(L && R, "never L and R at " + deg);
            }
        }

        [Test]
        public void Cardinals_own_50_degrees_and_diagonals_40() {
            Assert.AreEqual(0, InputLogic.SectorIndex(0));
            Assert.AreEqual(0, InputLogic.SectorIndex(24));
            Assert.AreEqual(1, InputLogic.SectorIndex(26));
            Assert.AreEqual(1, InputLogic.SectorIndex(45));
            Assert.AreEqual(2, InputLogic.SectorIndex(90));
            Assert.AreEqual(4, InputLogic.SectorIndex(180));
            Assert.AreEqual(6, InputLogic.SectorIndex(270));
            Assert.AreEqual(7, InputLogic.SectorIndex(315));
        }

        [Test]
        public void Dead_zone_produces_neutral() {
            bool U, D, L, R;
            InputLogic.Quantise8(new Vector2(0.1f, 0.1f), 0.25f, out U, out D, out L, out R);
            Assert.IsFalse(U || D || L || R);
        }

        // ---- 2. SOCD modes 0-4 -----------------------------------------------------
        /// <summary>Runs a sequence of (L,R) presses through one resolver and returns the last result.</summary>
        static (bool L, bool R) SocdSequence(int method, params (bool L, bool R)[] ticks) {
            var s = new InputLogic.Socd();
            bool L = false, R = false;
            foreach (var t in ticks) {
                bool u = false, d = false;
                L = t.L; R = t.R;
                s.Resolve(method, ref u, ref d, ref L, ref R);
            }
            return (L, R);
        }

        static (bool U, bool D) SocdSequenceUD(int method, params (bool U, bool D)[] ticks) {
            var s = new InputLogic.Socd();
            bool U = false, D = false;
            foreach (var t in ticks) {
                bool l = false, r = false;
                U = t.U; D = t.D;
                s.Resolve(method, ref U, ref D, ref l, ref r);
            }
            return (U, D);
        }

        [Test]
        public void Socd_mode0_allows_both() {
            var r = SocdSequence(0, (true, true));
            Assert.IsTrue(r.L && r.R);
        }

        [Test]
        public void Socd_mode4_default_denies_both() {
            var r = SocdSequence(4, (true, true));
            Assert.IsFalse(r.L || r.R);
            var ud = SocdSequenceUD(4, (true, true));
            Assert.IsFalse(ud.U || ud.D);
        }

        [Test]
        public void Socd_mode1_last_direction_wins() {
            // hold B, then add F: F (the newer one) must win
            var r = SocdSequence(1, (true, false), (true, true));
            Assert.IsFalse(r.L, "back released");
            Assert.IsTrue(r.R, "forward wins");
            // hold U, then add D: D must win
            var ud = SocdSequenceUD(1, (true, false), (true, true));
            Assert.IsFalse(ud.U);
            Assert.IsTrue(ud.D);
        }

        [Test]
        public void Socd_mode3_first_direction_wins() {
            var r = SocdSequence(3, (true, false), (true, true));
            Assert.IsTrue(r.L, "back held first keeps priority");
            Assert.IsFalse(r.R);
            var ud = SocdSequenceUD(3, (true, false), (true, true));
            Assert.IsTrue(ud.U);
            Assert.IsFalse(ud.D);
        }

        [Test]
        public void Socd_mode2_absolute_priority_forward_and_up() {
            var r = SocdSequence(2, (true, true));
            Assert.IsFalse(r.L);
            Assert.IsTrue(r.R, "offence (forward) over defence (back)");
            var ud = SocdSequenceUD(2, (true, true));
            Assert.IsTrue(ud.U);
            Assert.IsFalse(ud.D);
        }

        [Test]
        public void Socd_single_direction_is_never_eaten() {
            for (int method = 0; method <= 4; method++) {
                var r = SocdSequence(method, (false, true));
                Assert.IsTrue(r.R, "forward alone, method " + method);
            }
        }

        // ---- 3. Button assist ------------------------------------------------------
        static bool[] B(params int[] indices) {
            var v = new bool[9];
            foreach (var i in indices) v[i] = true;
            return v;
        }

        [Test]
        public void ButtonAssist_delays_a_press_by_one_tick_like_ikemen() {
            var assist = new InputLogic.ButtonAssist();
            var t0 = assist.Check(B(3));                 // x pressed on tick 0
            Assert.IsFalse(t0[3], "press is buffered, not reported on the same tick");
            var t1 = assist.Check(B(3));
            Assert.IsTrue(t1[3], "reported on the next tick");
        }

        [Test]
        public void ButtonAssist_merges_x_and_y_pressed_one_tick_apart() {
            var assist = new InputLogic.ButtonAssist();
            assist.Check(B(3));                          // x on tick 0
            var t1 = assist.Check(B(3, 4));              // y added on tick 1
            Assert.IsTrue(t1[3] && t1[4], "x+y arrive together");
        }

        [Test]
        public void ButtonAssist_disabled_passes_input_through() {
            var assist = new InputLogic.ButtonAssist();
            var t0 = assist.Check(B(3), enabled: false);
            Assert.IsTrue(t0[3]);
        }

        [Test]
        public void ButtonAssist_is_bypassed_while_paused() {
            var assist = new InputLogic.ButtonAssist();
            var t0 = assist.Check(B(5), true, paused: true);
            Assert.IsTrue(t0[5]);
        }

        // ---- 4. Tick latching ------------------------------------------------------
        [Test]
        public void A_tap_between_two_ticks_is_still_seen() {
            var latched = new InputFrame();
            var tap = new InputFrame { x = true };
            latched = latched.Or(tap);                    // pressed in render frame 1
            latched = latched.Or(new InputFrame());       // released in render frame 2
            Assert.IsTrue(latched.x, "the press survives until the next logic tick");
        }

        [Test]
        public void Bits_round_trip_matches_ikemen_order() {
            var f = new InputFrame { U = true, R = true, x = true, m = true };
            int bits = f.ToBits();
            Assert.AreEqual(1 << 0 | 1 << 3 | 1 << 7 | 1 << 13, bits);
            var back = InputFrame.FromBits(bits);
            Assert.IsTrue(back.U && back.R && back.x && back.m);
            Assert.IsFalse(back.D || back.L || back.y);
        }

        // ---- 5. Command recogniser on synthetic touch traces ------------------------
        static InputFrame Dir(string token, bool x = false, bool y = false) {
            var f = new InputFrame { x = x, y = y };
            switch (token) {
                case "D": f.D = true; break;
                case "DF": f.D = true; f.R = true; break;
                case "DB": f.D = true; f.L = true; break;
                case "F": f.R = true; break;
                case "B": f.L = true; break;
                case "U": f.U = true; break;
                case "N": break;
            }
            return f;
        }

        static void Feed(CommandRecognizer r, params InputFrame[] frames) {
            foreach (var f in frames) r.Push(f);
        }

        [Test]
        public void QCF_punch_is_recognised_within_command_time_15() {
            var r = CommandRecognizer.Kfm();
            Feed(r, Dir("N"), Dir("D"), Dir("D"), Dir("DF"), Dir("DF"), Dir("F"), Dir("F", x: true));
            CollectionAssert.Contains(new List<string>(r.Recognised), "QCF+x (Kouken)");
        }

        [Test]
        public void QCB_y_is_recognised() {
            var r = CommandRecognizer.Kfm();
            Feed(r, Dir("N"), Dir("D"), Dir("DB"), Dir("B"), Dir("B", y: true));
            CollectionAssert.Contains(new List<string>(r.Recognised), "QCB+y (Kouken back)");
        }

        [Test]
        public void Dragon_punch_is_recognised() {
            var r = CommandRecognizer.Kfm();
            Feed(r, Dir("N"), Dir("F"), Dir("D"), Dir("DF"), Dir("DF", x: true));
            CollectionAssert.Contains(new List<string>(r.Recognised), "DP+x (Shoryuken)");
        }

        [Test]
        public void Super_double_qcf_is_recognised() {
            var r = CommandRecognizer.Kfm();
            Feed(r, Dir("N"), Dir("D"), Dir("DF"), Dir("F"), Dir("D"), Dir("DF"), Dir("F"), Dir("F", x: true));
            CollectionAssert.Contains(new List<string>(r.Recognised), "Super (QCFx2+x)");
        }

        [Test]
        public void Simultaneous_x_plus_y_is_recognised() {
            var r = CommandRecognizer.Kfm();
            Feed(r, Dir("N"), Dir("N", x: true, y: true));
            CollectionAssert.Contains(new List<string>(r.Recognised), "x+y");
        }

        [Test]
        public void A_slow_motion_is_not_recognised_outside_command_time() {
            var r = CommandRecognizer.Kfm();
            r.Push(Dir("D"));
            r.Push(Dir("DF"));
            for (int i = 0; i < 20; i++) r.Push(Dir("N"));   // the motion is abandoned
            r.Push(Dir("F"));
            r.Push(Dir("F", x: true));
            CollectionAssert.DoesNotContain(new List<string>(r.Recognised), "QCF+x (Kouken)");
        }
    }
}
