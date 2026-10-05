using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;
using IK.UI;

namespace IK.Tests {
    /// <summary>
    /// dev.4 gate for the screenpack HUD geometry. Every expected number is either read out of
    /// assets/screenpack/data/fight.def in the test itself (so the assertion breaks when the
    /// motif changes) or derived from the Go reference engine/ikemen-go/src/fightscreen.go
    /// (`calcBarFillRect`, `LifeBar.draw`, `PowerBar.draw`, `resolvePBKey`,
    /// `FightScreenTime.draw`) and font.go (`Fnt.TextWidth`, `Fnt.DrawText`).
    ///
    /// The uGUI side of <see cref="FightHud"/> is not exercised here: EditMode cannot build a
    /// canvas cheaply, so the layout maths lives in pure static methods and those are tested.
    /// </summary>
    public class FightHudTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string DataDir => Path.Combine(Repo, "assets", "screenpack", "data");

        static FightDef cached;
        static FightDef Fight {
            get {
                if (cached == null) {
                    string path = Path.Combine(DataDir, "fight.def");
                    Assert.IsTrue(File.Exists(path), "missing " + path);
                    cached = FightDef.Load(File.ReadAllBytes(path), new FileSource(DataDir));
                }
                return cached;
            }
        }

        static HudFont LoadFont(int index) {
            var path = Fight.Files.Fonts[index];
            var f = HudFont.Load(new FileSource(DataDir), path);
            Assert.IsNotNull(f, "font " + index + " (" + path + ") did not load");
            return f;
        }

        // ---- calcBarFillRect ------------------------------------------------------

        [Test]
        public void P1_life_bar_fill_runs_right_to_left_from_the_def_range() {
            var lb = Fight.LifeBar[0];
            // fight.def [Lifebar]: p1.pos = 595,40 and p1.range.x = 15,-460 (a descending range)
            Assert.AreEqual(595, lb.Pos[0]);
            Assert.AreEqual(40, lb.Pos[1]);
            Assert.AreEqual(15, lb.RangeX[0]);
            Assert.AreEqual(-460, lb.RangeX[1]);

            // Go calcBarFillRect: the range is inclusive, so a full bar is 15 - (-460) + 1 long
            Assert.AreEqual(476f, FightHud.BarFillLength(lb.RangeX), 1e-4f);
            Assert.AreEqual(476f, FightHud.BarWidth(lb.RangeX, 1f), 1e-4f);
            Assert.AreEqual(238f, FightHud.BarWidth(lb.RangeX, 0.5f), 1e-4f);

            // descending: start = pos + high + 1 - size, so the right edge stays at 611
            Assert.AreEqual(135f, FightHud.BarStart(lb.Pos[0], lb.RangeX, 1f), 1e-4f);
            Assert.AreEqual(373f, FightHud.BarStart(lb.Pos[0], lb.RangeX, 0.5f), 1e-4f);
            Assert.AreEqual(611f, FightHud.BarStart(lb.Pos[0], lb.RangeX, 0f), 1e-4f);
        }

        [Test]
        public void P2_life_bar_is_mirrored_by_its_own_def_coordinates() {
            var lb = Fight.LifeBar[1];
            // fight.def: p2.pos = 684,40 and p2.range.x = -15,460 (ascending, the mirror image)
            Assert.AreEqual(684, lb.Pos[0]);
            Assert.AreEqual(-15, lb.RangeX[0]);
            Assert.AreEqual(460, lb.RangeX[1]);

            // ascending: the left edge is fixed at pos + low and the fill grows to the right
            Assert.AreEqual(669f, FightHud.BarStart(lb.Pos[0], lb.RangeX, 1f), 1e-4f);
            Assert.AreEqual(669f, FightHud.BarStart(lb.Pos[0], lb.RangeX, 0.25f), 1e-4f);
            Assert.AreEqual(476f, FightHud.BarWidth(lb.RangeX, 1f), 1e-4f);
            Assert.AreEqual(119f, FightHud.BarWidth(lb.RangeX, 0.25f), 1e-4f);

            // both bars are the same length and meet in the middle of the 1280 wide screen
            Assert.AreEqual(FightHud.BarFillLength(Fight.LifeBar[0].RangeX),
                            FightHud.BarFillLength(lb.RangeX), 1e-4f);
        }

        [Test]
        public void Power_bar_ranges_come_from_the_def_for_both_sides() {
            var p1 = Fight.PowerBar[0];
            var p2 = Fight.PowerBar[1];
            // fight.def [Powerbar]: p1.pos = 568,83 / p1.range.x = -4,-206
            //                       p2.pos = 712,83 / p2.range.x = 4,206
            Assert.AreEqual(568, p1.Pos[0]);
            Assert.AreEqual(83, p1.Pos[1]);
            Assert.AreEqual(-4, p1.RangeX[0]);
            Assert.AreEqual(-206, p1.RangeX[1]);
            Assert.AreEqual(712, p2.Pos[0]);
            Assert.AreEqual(4, p2.RangeX[0]);
            Assert.AreEqual(206, p2.RangeX[1]);

            Assert.AreEqual(203f, FightHud.BarFillLength(p1.RangeX), 1e-4f);   // -4 - (-206) + 1
            Assert.AreEqual(203f, FightHud.BarFillLength(p2.RangeX), 1e-4f);
            Assert.AreEqual(362f, FightHud.BarStart(p1.Pos[0], p1.RangeX, 1f), 1e-4f);  // 568-4+1-203
            Assert.AreEqual(565f, FightHud.BarStart(p1.Pos[0], p1.RangeX, 0f), 1e-4f);
            Assert.AreEqual(716f, FightHud.BarStart(p2.Pos[0], p2.RangeX, 1f), 1e-4f);  // 712+4
        }

        [Test]
        public void Unset_ranges_are_not_clipped() {
            // fight.def never gives the bars a range.y, so the vertical axis is unclipped
            Assert.IsFalse(FightHud.RangeIsSet(Fight.LifeBar[0].RangeY));
            Assert.IsFalse(FightHud.RangeIsSet(Fight.PowerBar[0].RangeY));
            Assert.IsTrue(FightHud.RangeIsSet(Fight.LifeBar[0].RangeX));
            Assert.AreEqual(0f, FightHud.BarFillLength(Fight.LifeBar[0].RangeY), 1e-4f);
            // an unset range leaves the start at the bar position itself
            Assert.AreEqual(40f, FightHud.BarStart(Fight.LifeBar[0].Pos[1], Fight.LifeBar[0].RangeY, 1f), 1e-4f);
        }

        // ---- element placement ----------------------------------------------------

        [Test]
        public void Element_position_is_pos_plus_offset_with_y_down() {
            var lb = Fight.LifeBar[0];
            // p1.bg0.offset = 0,0 -> the bar background sits exactly on p1.pos
            Assert.AreEqual(0f, lb.Bg0.Layout.OffsetX, 1e-4f);
            Assert.AreEqual(0f, lb.Bg0.Layout.OffsetY, 1e-4f);
            var p = FightHud.ElementPosition(lb.Pos, lb.Bg0.Layout, 1f);
            Assert.AreEqual(595f, p.x, 1e-4f);
            Assert.AreEqual(-40f, p.y, 1e-4f);          // uGUI y grows upwards, MUGEN y downwards

            var scaled = FightHud.ElementPosition(lb.Pos, lb.Bg0.Layout, 2f);
            Assert.AreEqual(1190f, scaled.x, 1e-4f);
            Assert.AreEqual(-80f, scaled.y, 1e-4f);

            // p1.front.offset = 0,0 but the power bar's front is shifted: p1.front.offset = -4,4
            var pb = Fight.PowerBar[0];
            var front = pb.Front[0];
            Assert.AreEqual(-4f, front.Layout.OffsetX, 1e-4f);
            Assert.AreEqual(4f, front.Layout.OffsetY, 1e-4f);
            var fp = FightHud.ElementPosition(pb.Pos, front.Layout, 1f);
            Assert.AreEqual(564f, fp.x, 1e-4f);         // 568 - 4
            Assert.AreEqual(-87f, fp.y, 1e-4f);         // -(83 + 4)
        }

        [Test]
        public void P2_elements_are_mirrored_with_facing_from_the_def() {
            // fight.def mirrors player 2 with its own facing flags, not by guessing
            Assert.AreEqual(1, Fight.LifeBar[0].Bg0.Layout.Facing);
            Assert.AreEqual(-1, Fight.LifeBar[1].Bg0.Layout.Facing);
            Assert.AreEqual(-1, Fight.LifeBar[1].Mid.Layout.Facing);
            Assert.AreEqual(-1, Fight.PowerBar[1].Bg0[0].Layout.Facing);
            // p2.frontMax is the one element the motif deliberately leaves unflipped
            Assert.AreEqual(1, Fight.PowerBar[1].Front[PowerBarDef.MaxKey].Layout.Facing);
        }

        [Test]
        public void Win_icon_slots_step_by_the_def_icon_offset() {
            var wi = Fight.WinIcon[0];
            // fight.def [WinIcon]: p1.pos = 544,120 and p1.iconoffset = -32,0
            Assert.AreEqual(544, wi.Pos[0]);
            Assert.AreEqual(120, wi.Pos[1]);
            Assert.AreEqual(-32, wi.IconOffset[0]);
            Assert.AreEqual(0, wi.IconOffset[1]);
            Assert.AreEqual(4, wi.UseIconUpTo);

            var slot2 = new[] { wi.Pos[0] + wi.IconOffset[0] * 2, wi.Pos[1] + wi.IconOffset[1] * 2 };
            var p = FightHud.ElementPosition(slot2, wi.Bg0.Layout, 1f);
            Assert.AreEqual(480f, p.x, 1e-4f);          // 544 - 64
            Assert.AreEqual(-120f, p.y, 1e-4f);
        }

        // ---- element variant selection --------------------------------------------

        [Test]
        public void Life_front_variant_follows_the_def_percentage_keys() {
            var keys = new List<float>(Fight.LifeBar[0].Front.Keys);
            // fight.def defines p1.front100, p1.front50, p1.front25 and the plain p1.front
            CollectionAssert.AreEquivalent(new[] { 0f, 25f, 50f, 100f }, keys);
            Assert.AreEqual(100f, FightHud.LifeFrontKey(keys, 1f), 1e-4f);
            Assert.AreEqual(50f, FightHud.LifeFrontKey(keys, 0.75f), 1e-4f);
            Assert.AreEqual(50f, FightHud.LifeFrontKey(keys, 0.5f), 1e-4f);
            Assert.AreEqual(25f, FightHud.LifeFrontKey(keys, 0.26f), 1e-4f);
            Assert.AreEqual(0f, FightHud.LifeFrontKey(keys, 0.1f), 1e-4f);
            Assert.AreEqual(0f, FightHud.LifeFrontKey(keys, 0f), 1e-4f);
        }

        [Test]
        public void Power_counter_variant_follows_resolve_pb_key() {
            var pb = Fight.PowerBar[0];
            var keys = new List<int>(pb.Counter.Keys);
            // fight.def has p1.counter. plus counter1000..counter10000 and counterMax
            Assert.Contains(PowerBarDef.MaxKey, keys, "counterMax must map to the max key");
            Assert.Contains(1000, keys);
            Assert.Contains(3000, keys);
            Assert.AreEqual("M", pb.Counter[PowerBarDef.MaxKey].Text);   // p1.counterMax.text = M

            // Go resolvePBKey: the highest key at or below the current power, max only at powerMax
            Assert.AreEqual(0, FightHud.PowerBarKey(keys, 0, 3000));
            Assert.AreEqual(1000, FightHud.PowerBarKey(keys, 1500, 3000));
            Assert.AreEqual(2000, FightHud.PowerBarKey(keys, 2999, 3000));
            Assert.AreEqual(PowerBarDef.MaxKey, FightHud.PowerBarKey(keys, 3000, 3000));
            // with a bigger pool the same power is just another level, never "max"
            Assert.AreEqual(3000, FightHud.PowerBarKey(keys, 3000, 10000));
        }

        [Test]
        public void Power_bar_fill_uses_levelbars_from_the_def() {
            var pb = Fight.PowerBar[0];
            Assert.IsTrue(pb.LevelBars, "fight.def sets p1.levelbars = 1");
            // Go PowerBar.draw: with levelbars the bar shows progress inside the current level
            Assert.AreEqual(0f, FightHud.LevelBarFill(0, 3000, true), 1e-4f);
            Assert.AreEqual(0.5f, FightHud.LevelBarFill(500, 3000, true), 1e-4f);
            Assert.AreEqual(0.5f, FightHud.LevelBarFill(2500, 3000, true), 1e-4f);
            Assert.AreEqual(1f, FightHud.LevelBarFill(3000, 3000, true), 1e-4f);
            // without levelbars it is the plain fraction
            Assert.AreEqual(0.5f, FightHud.LevelBarFill(1500, 3000, false), 1e-4f);
        }

        [Test]
        public void Time_counter_variant_follows_the_def_counter_keys() {
            var keys = new List<int>(Fight.Time.Counter.Keys);
            // fight.def [Time] defines counter., counter1, counter10 and counter11
            CollectionAssert.AreEquivalent(new[] { 0, 1, 10, 11 }, keys);
            Assert.AreEqual(11, FightHud.TimeCounterKey(keys, 99));
            Assert.AreEqual(11, FightHud.TimeCounterKey(keys, 11));
            Assert.AreEqual(10, FightHud.TimeCounterKey(keys, 10));
            Assert.AreEqual(1, FightHud.TimeCounterKey(keys, 5));
            Assert.AreEqual(0, FightHud.TimeCounterKey(keys, 0));
            Assert.AreEqual(11, FightHud.TimeCounterKey(keys, -1));   // infinite timer: highest key
            Assert.AreEqual(60, Fight.Time.FramesPerCount);           // framespercount = 60
        }

        // ---- announcement timing ---------------------------------------------------

        [Test]
        public void Round_and_fight_calls_use_the_def_times() {
            var ro = Fight.Round;
            // fight.def [Round]: round.time = 0, callfight.time = 60, round.default.displaytime = 55
            Assert.AreEqual(0, ro.RoundTime);
            Assert.AreEqual(60, ro.CallFightTime);
            Assert.AreEqual(55, ro.RoundDefault.DisplayTime);
            Assert.AreEqual(0, ro.FightTime);
            Assert.AreEqual("R   %i", ro.RoundDefault.Text.Text);

            // the intro clock runs from the start of the announce phase into the fight
            // the intro clock is the round clock: it keeps running through the whole round so
            // the "Fight!" call cannot come back when the round state machine moves on
            Assert.AreEqual(-1, FightHud.IntroTick(RoundState.Intro, 0));
            Assert.AreEqual(0, FightHud.IntroTick(RoundState.Announce, 0));
            Assert.AreEqual(59, FightHud.IntroTick(RoundState.Announce, 59));
            Assert.AreEqual(60, FightHud.IntroTick(RoundState.Fighting, 60));
            Assert.AreEqual(900, FightHud.IntroTick(RoundState.Over, 900));

            // "Round 1" shows for displaytime ticks from round.time
            Assert.IsTrue(FightHud.ElementVisible(0, ro.RoundTime, ro.RoundDefault.DisplayTime, 0));
            Assert.IsTrue(FightHud.ElementVisible(55, ro.RoundTime, ro.RoundDefault.DisplayTime, 0));
            Assert.IsFalse(FightHud.ElementVisible(56, ro.RoundTime, ro.RoundDefault.DisplayTime, 0));
            // "Fight" only after callfight.time
            Assert.IsFalse(FightHud.ElementVisible(59, ro.RoundTime + ro.CallFightTime, -2, 40));
            Assert.IsTrue(FightHud.ElementVisible(60, ro.RoundTime + ro.CallFightTime, -2, 40));
            Assert.IsFalse(FightHud.ElementVisible(100, ro.RoundTime + ro.CallFightTime, -2, 40));
        }

        [Test]
        public void Ko_and_win_announcements_use_the_def_times() {
            var ro = Fight.Round;
            // fight.def [Round]: KO.time = 0, TO.displaytime = 70, DKO.displaytime = 120,
            //                    win.time = 60, p1.win.displaytime = 180
            Assert.AreEqual(0, ro.KoTime);
            Assert.AreEqual(70, ro.To.DisplayTime);
            Assert.AreEqual(120, ro.Dko.DisplayTime);
            Assert.AreEqual(60, ro.WinTime);
            Assert.AreEqual(180, ro.Win[0].Text[0].DisplayTime);
            Assert.AreEqual("%s", ro.Win[0].Text[0].Text.Text);

            // the outro clock runs from the KO through the win pose
            Assert.AreEqual(-1, FightHud.OutroTick(RoundState.Fighting, 10, 45));
            Assert.AreEqual(0, FightHud.OutroTick(RoundState.Over, 0, 45));
            Assert.AreEqual(45, FightHud.OutroTick(RoundState.WinPose, 0, 45));
            Assert.AreEqual(105, FightHud.OutroTick(RoundState.WinPose, 60, 45));

            // the winner banner starts win.time ticks after the round ended
            Assert.IsFalse(FightHud.ElementVisible(59, ro.WinTime, 180, 0));
            Assert.IsTrue(FightHud.ElementVisible(60, ro.WinTime, 180, 0));
            Assert.IsTrue(FightHud.ElementVisible(240, ro.WinTime, 180, 0));
            Assert.IsFalse(FightHud.ElementVisible(241, ro.WinTime, 180, 0));
            // "Time over" lasts its own displaytime
            Assert.IsTrue(FightHud.ElementVisible(70, ro.ToTime, ro.To.DisplayTime, 0));
            Assert.IsFalse(FightHud.ElementVisible(71, ro.ToTime, ro.To.DisplayTime, 0));
        }

        [Test]
        public void Round_text_takes_the_round_number() {
            var ro = Fight.Round;
            Assert.AreEqual("R   1", FightHud.ReplaceFirst(ro.RoundDefault.Text.Text, "%i", "1"));
            Assert.AreEqual("R   3", FightHud.ReplaceFirst(ro.RoundDefault.Text.Text, "%i", "3"));
            Assert.AreEqual("Kung Fu Man", FightHud.ReplaceFirst(ro.Win[0].Text[0].Text.Text, "%s", "Kung Fu Man"));
            Assert.AreEqual("no token", FightHud.ReplaceFirst("no token", "%i", "1"));
        }

        [Test]
        public void Ko_animation_length_comes_from_the_def_action() {
            var ro = Fight.Round;
            // fight.def: KO.anim = 529, fight.anim = 520
            Assert.AreEqual(529, ro.Ko.Anim.AnimNo);
            Assert.AreEqual(520, ro.Fight.Anim.AnimNo);
            var ko = Fight.Animations.Get(529);
            Assert.IsNotNull(ko, "[Begin Action 529] must exist in fight.def");
            Assert.Greater(FightHud.AnimDuration(ko), 0, "the KO action must have a duration");
            Assert.AreEqual(0, FightHud.AnimDuration(null));
        }

        // ---- screenpack fonts -------------------------------------------------------

        [Test]
        public void Timer_font_loads_its_real_metrics_and_glyphs() {
            // fight.def [Files]: font2 = ikemen1/fonts/Timer.def
            Assert.AreEqual("ikemen1/fonts/Timer.def", Fight.Files.Fonts[2]);
            var f = LoadFont(2);
            Assert.IsTrue(f.Ready);
            Assert.AreEqual("bitmap", f.Type);          // Timer.def [Def] Type = bitmap
            Assert.AreEqual("palette", f.BankType);     // BankType = palette
            Assert.AreEqual(35, f.SizeX);               // Size = 35,49
            Assert.AreEqual(49, f.SizeY);
            Assert.AreEqual(5, f.SpacingX);             // Spacing = 5,6
            Assert.AreEqual(6, f.SpacingY);
            Assert.AreEqual(0, f.OffsetX);              // Offset = 0,0
            Assert.AreEqual(0, f.OffsetY);

            // Timer.sff carries one sprite per character code in group 0
            var zero = f.Glyph('0');
            Assert.IsNotNull(zero, "Timer.sff must hold a glyph for '0'");
            Assert.AreEqual(35, zero.Width);
            Assert.AreEqual(49, zero.Height);
            Assert.AreEqual(23, f.CharWidth('1'));      // '1' is the narrow glyph
            Assert.AreEqual(0, f.CharWidth('Z'));       // the timer font has no letters
            Assert.AreEqual(35, f.CharWidth(' '));      // a space is Size[0] wide
            f.Dispose();
        }

        [Test]
        public void Text_width_matches_the_go_formula() {
            var timer = LoadFont(2);
            // Go Fnt.TextWidth: widths plus spacing between the glyphs, not after the last one
            Assert.AreEqual(35, timer.TextWidth("0"));
            Assert.AreEqual(75, timer.TextWidth("99"));        // 35 + 5 + 35
            Assert.AreEqual(51, timer.TextWidth("11"));        // 23 + 5 + 23
            Assert.AreEqual(0, timer.TextWidth(""));
            timer.Dispose();

            var round = LoadFont(9);
            // Round.def: Size = 45,180 and Spacing = -25,9 (a negative kerning)
            Assert.AreEqual(45, round.SizeX);
            Assert.AreEqual(180, round.SizeY);
            Assert.AreEqual(-25, round.SpacingX);
            // "R   1": 407 + 45 + 45 + 45 + 119 glyph widths and four -25 gaps
            Assert.AreEqual(407, round.CharWidth('R'));
            Assert.AreEqual(119, round.CharWidth('1'));
            Assert.AreEqual(561, round.TextWidth("R   1"));
            round.Dispose();
        }

        [Test]
        public void Timer_text_pen_follows_the_def_position_and_scale() {
            var ti = Fight.Time;
            var counter = ti.Counter[0];
            // fight.def [Time]: pos = 640,105 / counter.offset = 0,30 / counter.scale = 2,2
            //                   counter.font = 2,0,0 (font 2, bank 0, centre aligned)
            Assert.AreEqual(640, ti.Pos[0]);
            Assert.AreEqual(105, ti.Pos[1]);
            Assert.AreEqual(0f, counter.Layout.OffsetX, 1e-4f);
            Assert.AreEqual(30f, counter.Layout.OffsetY, 1e-4f);
            Assert.AreEqual(2f, counter.Layout.ScaleX, 1e-4f);
            Assert.AreEqual(2, counter.FontIndex);
            Assert.AreEqual(0, counter.FontAlign);

            var f = LoadFont(2);
            float xscl = counter.Layout.ScaleX, yscl = counter.Layout.ScaleY;
            // centred: the pen moves left by half the string
            Assert.AreEqual(565f, FightHud.TextPenX(f, "99", ti.Pos[0] + counter.Layout.OffsetX, xscl, 0), 1e-4f);
            // left aligned would start right on the position
            Assert.AreEqual(640f, FightHud.TextPenX(f, "99", ti.Pos[0] + counter.Layout.OffsetX, xscl, 1), 1e-4f);
            // right aligned ends on it
            Assert.AreEqual(490f, FightHud.TextPenX(f, "99", ti.Pos[0] + counter.Layout.OffsetX, xscl, -1), 1e-4f);
            // Go Fnt.DrawText: the given y is the bottom of the line, the pen sits a line above
            Assert.AreEqual(39f, FightHud.TextPenY(f, ti.Pos[1] + counter.Layout.OffsetY, yscl), 1e-4f);
            f.Dispose();
        }

        [Test]
        public void Name_fonts_are_aligned_outwards_by_the_def() {
            // fight.def [Name]: p1.pos = 13,145 / p1.name.font = 3,0,1 (left aligned)
            //                   p2.pos = 1267,145 / p2.name.font = 3,0,-1 (right aligned)
            Assert.AreEqual(13, Fight.Names[0].Pos[0]);
            Assert.AreEqual(145, Fight.Names[0].Pos[1]);
            Assert.AreEqual(1267, Fight.Names[1].Pos[0]);
            Assert.AreEqual(3, Fight.Names[0].Name.FontIndex);
            Assert.AreEqual(1, Fight.Names[0].Name.FontAlign);
            Assert.AreEqual(-1, Fight.Names[1].Name.FontAlign);

            var f = LoadFont(3);
            Assert.IsTrue(f.Ready, "font3 (Menu2Small) must load");
            int w = f.TextWidth("KFM");
            Assert.Greater(w, 0);
            // left aligned starts on the position, right aligned ends on it
            Assert.AreEqual(13f, FightHud.TextPenX(f, "KFM", 13f, 1f, 1), 1e-4f);
            Assert.AreEqual(1267f - w, FightHud.TextPenX(f, "KFM", 1267f, 1f, -1), 1e-4f);
            f.Dispose();
        }

        [Test]
        public void Font_sff_is_resolved_next_to_its_def() {
            Assert.AreEqual("ikemen1/fonts/Timer.sff", HudFont.SiblingPath("ikemen1/fonts/Timer.def", "Timer.sff"));
            Assert.AreEqual("Timer.sff", HudFont.SiblingPath("Timer.def", "Timer.sff"));
            Assert.AreEqual("a/b/c.sff", HudFont.SiblingPath("a\\b\\x.def", "c.sff"));
            Assert.IsNull(HudFont.Load(new FileSource(DataDir), "ikemen1/fonts/NoSuchFont.def"));
        }

        // ---- API guards --------------------------------------------------------------

        [Test]
        public void Hud_without_data_is_not_ready() {
            var hud = new FightHud(null, null, null, null);
            Assert.IsFalse(hud.Ready);
            Assert.IsNotNull(hud.LoadError);
            hud.Draw(null);                 // must not throw
            hud.SetVisible(true);
            hud.Dispose();
        }
    }
}
