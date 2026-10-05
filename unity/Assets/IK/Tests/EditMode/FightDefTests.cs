using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.4 gate for the fight screen loader. Every expected number is quoted from
    /// assets/screenpack/data/fight.def (line numbers in the comments) or derived from the
    /// Go reference engine/ikemen-go/src/fightscreen.go (defaults and LifeBar.step).
    /// </summary>
    public class FightDefTests {
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

        static void AssertSpr(FightAnimLayout al, int g, int n, string what) {
            Assert.IsNotNull(al, what);
            Assert.IsTrue(al.HasSprite, what + " has a sprite");
            Assert.AreEqual(g, al.SprGroup, what + " group");
            Assert.AreEqual(n, al.SprNumber, what + " number");
        }

        // ---- [Info] / [Files] -----------------------------------------------------

        [Test]
        public void Info_and_files_are_read() {
            var f = Fight;
            Assert.AreEqual("IKEMEN1", f.MotifName);          // line 21
            Assert.AreEqual(1280, f.LocalcoordX);              // line 25
            Assert.AreEqual(720, f.LocalcoordY);
            Assert.AreEqual("fight.sff", f.Files.Sff);         // lines 40-52
            Assert.AreEqual("fight.snd", f.Files.Snd);
            Assert.AreEqual("fightfx.sff", f.Files.FightFxSff);
            Assert.AreEqual("fightfx.air", f.Files.FightFxAir);
            Assert.AreEqual("common.snd", f.Files.CommonSnd);
            Assert.AreEqual(0, f.Files.Fx.Count, "fx1 is commented out");
            Assert.AreEqual(4f, f.FightFxScale, 1e-6f);        // [FightFx] scale = 4
        }

        [Test]
        public void Font_list_has_nine_entries() {
            var fonts = Fight.Files.Fonts;
            Assert.AreEqual(9, fonts.Count);                   // font1..font9, lines 42-50
            Assert.AreEqual("ikemen1/fonts/Menu2.def", fonts[1]);
            Assert.AreEqual("ikemen1/fonts/Timer.def", fonts[2]);
            Assert.AreEqual("ikemen1/fonts/PowerbarNum.def", fonts[4]);
            Assert.AreEqual("ikemen1/fonts/Round.def", fonts[9]);
        }

        [Test]
        public void Section_count_and_embedded_animations() {
            var f = Fight;
            // 87 bracketed sections in fight.def, 38 of them [Begin Action n].
            Assert.AreEqual(87, f.SectionCount);
            Assert.IsNotNull(f.Animations.Get(520), "fight.anim = 520");
            Assert.IsNotNull(f.Animations.Get(529), "KO.anim = 529");
            Assert.IsNotNull(f.Animations.Get(544), "draw.anim = 544");
        }

        // ---- [Lifebar] -------------------------------------------------------------

        [Test]
        public void Lifebar_p1_elements() {
            var lb = Fight.LifeBar[0];
            Assert.AreEqual(595, lb.Pos[0]); Assert.AreEqual(40, lb.Pos[1]);   // p1.pos = 595,40
            AssertSpr(lb.Bg0, 10, 0, "p1.bg0");
            Assert.AreEqual(0f, lb.Bg0.Layout.OffsetX); Assert.AreEqual(0f, lb.Bg0.Layout.OffsetY);
            Assert.AreEqual(3f, lb.Bg0.Layout.XShear, 1e-6f);                    // p1.bg0.xshear = 3
            AssertSpr(lb.Top, 11, 0, "p1.top");
            AssertSpr(lb.Mid, 12, 0, "p1.mid");
            Assert.IsFalse(lb.Bg1.HasFrames, "p1.bg1 is not defined");
            Assert.AreEqual(15, lb.RangeX[0]); Assert.AreEqual(-460, lb.RangeX[1]);  // p1.range.x = 15,-460
            Assert.IsTrue(lb.ScaleFill);
        }

        [Test]
        public void Lifebar_front_variants_are_keyed_by_percentage() {
            var lb = Fight.LifeBar[0];
            Assert.AreEqual(4, lb.Front.Count);               // front., front25., front50., front100.
            AssertSpr(lb.Front[0f], 13, 3, "p1.front");
            AssertSpr(lb.Front[25f], 13, 2, "p1.front25");
            AssertSpr(lb.Front[50f], 13, 1, "p1.front50");
            AssertSpr(lb.Front[100f], 13, 0, "p1.front100");
        }

        [Test]
        public void Lifebar_p2_is_mirrored() {
            var lb = Fight.LifeBar[1];
            Assert.AreEqual(684, lb.Pos[0]); Assert.AreEqual(40, lb.Pos[1]);   // p2.pos = 684,40
            AssertSpr(lb.Bg0, 10, 0, "p2.bg0");
            Assert.AreEqual(-1, lb.Bg0.Layout.Facing);                           // p2.bg0.facing = -1
            Assert.AreEqual(-3f, lb.Bg0.Layout.XShear, 1e-6f);
            AssertSpr(lb.Mid, 12, 0, "p2.mid");
            Assert.AreEqual(-1, lb.Mid.Layout.Facing);
            AssertSpr(lb.FrontDefault, 13, 3, "p2.front");
            Assert.AreEqual(-15, lb.RangeX[0]); Assert.AreEqual(460, lb.RangeX[1]);
            Assert.AreEqual(1, Fight.LifeBar[0].Bg0.Layout.Facing, "p1 keeps the default facing");
        }

        [Test]
        public void Lifebar_mid_parameters_use_go_defaults() {
            // [Lifebar] has no mid.* keys (they only appear in [Tag Lifebar] / [Tag_3P Lifebar]),
            // so newLifeBar's defaults apply: freeze = true, delay = 30, mult = 1, steps = 8.
            var lb = Fight.LifeBar[0];
            Assert.IsTrue(lb.MidFreeze);
            Assert.AreEqual(30, lb.MidDelay);
            Assert.AreEqual(1f, lb.MidMult, 1e-6f);
            Assert.AreEqual(8f, lb.MidSteps, 1e-6f);
        }

        // ---- [Powerbar] ------------------------------------------------------------

        [Test]
        public void Powerbar_elements_and_level_sounds() {
            var pb = Fight.PowerBar[0];
            Assert.AreEqual(568, pb.Pos[0]); Assert.AreEqual(83, pb.Pos[1]);      // p1.pos = 568,83
            AssertSpr(pb.Bg0[0], 40, 0, "p1.bg0");
            Assert.AreEqual(3f, pb.Bg0[0].Layout.OffsetY);                       // p1.bg0.offset = 0,3
            AssertSpr(pb.Bg1, 41, 0, "p1.bg1");
            AssertSpr(pb.Front[0], 43, 0, "p1.front");
            Assert.AreEqual(-4f, pb.Front[0].Layout.OffsetX);                    // p1.front.offset = -4,4
            // front., front500. .. front10000. (11 numbered + default) and frontMax.
            Assert.AreEqual(13, pb.Front.Count);
            AssertSpr(pb.Front[PowerBarDef.MaxKey], 44, 0, "p1.frontMax");
            Assert.AreEqual("M", pb.Counter[PowerBarDef.MaxKey].Text);          // p1.counterMax.text = M
            Assert.AreEqual(4, pb.Counter[0].FontIndex);                         // p1.counter.font = 4,0,0
            Assert.AreEqual(-4, pb.RangeX[0]); Assert.AreEqual(-206, pb.RangeX[1]);
            for (int i = 0; i < 9; i++) {                                        // level1..9.snd = 21,0
                Assert.AreEqual(21, pb.LevelSnd[i][0], "level" + (i + 1));
                Assert.AreEqual(0, pb.LevelSnd[i][1], "level" + (i + 1));
            }
            Assert.AreEqual(21, pb.LevelMaxSnd[0]); Assert.AreEqual(1, pb.LevelMaxSnd[1]); // levelMax.snd = 21, 1
            Assert.IsTrue(Fight.PowerBar[1].LevelBars);                          // p2.levelbars = 1
        }

        // ---- [Face] / [Name] / [WinIcon] -------------------------------------------

        [Test]
        public void Face_name_and_winicon() {
            var f = Fight;
            Assert.AreEqual(20, f.Face[0].Pos[0]); Assert.AreEqual(20, f.Face[0].Pos[1]);   // p1.pos = 20,20
            AssertSpr(f.Face[0].Bg0, 50, 0, "face p1.bg0");
            Assert.AreEqual(9000, f.Face[0].FaceSpr[0]); Assert.AreEqual(0, f.Face[0].FaceSpr[1]);
            Assert.AreEqual(1260, f.Face[1].Pos[0]);
            Assert.AreEqual(-1, f.Face[1].FaceLayout.Facing);                   // p2.face.facing = -1

            Assert.AreEqual(13, f.Names[0].Pos[0]); Assert.AreEqual(145, f.Names[0].Pos[1]);
            Assert.AreEqual(3, f.Names[0].Name.FontIndex);                       // p1.name.font = 3,0, 1
            Assert.AreEqual(1, f.Names[0].Name.FontAlign);
            Assert.AreEqual(-1, f.Names[1].Name.FontAlign);                      // p2.name.font = 3,0, -1

            var wi = f.WinIcon[0];
            Assert.AreEqual(544, wi.Pos[0]); Assert.AreEqual(120, wi.Pos[1]);
            Assert.AreEqual(-32, wi.IconOffset[0]);
            Assert.AreEqual(4, wi.UseIconUpTo);
            AssertSpr(wi.Icon[(int)FightWinType.Normal], 100, 0, "p1.n");
            AssertSpr(wi.Icon[(int)FightWinType.Throw], 103, 0, "p1.throw");
            AssertSpr(wi.Icon[(int)FightWinType.Perfect], 110, 0, "p1.perfect");
            Assert.AreEqual(230, wi.Counter.Font[3]);                            // 4,0,0, 230,255,242
            Assert.AreEqual(242, wi.Counter.Font[5]);
        }

        // ---- [Time] ----------------------------------------------------------------

        [Test]
        public void Time_counter_font_and_tick_rate() {
            var t = Fight.Time;
            Assert.AreEqual(640, t.Pos[0]); Assert.AreEqual(105, t.Pos[1]);       // pos = 640,105
            Assert.AreEqual(60, t.FramesPerCount);                               // framespercount = 60
            Assert.AreEqual(2, t.Counter[0].FontIndex);                          // counter.font = 2,0, 0
            Assert.AreEqual(0, t.Counter[0].FontBank);
            Assert.AreEqual(30f, t.Counter[0].Layout.OffsetY);                   // counter.offset = 0,30
            Assert.AreEqual(2f, t.Counter[0].Layout.ScaleX);                     // counter.scale = 2, 2
            CollectionAssert.AreEqual(new[] { 0, 1, 10, 11 }, new System.Collections.Generic.List<int>(t.Counter.Keys));
            Assert.AreEqual(255, t.Counter[11].Font[3]);                         // counter11.font = 2,0,0,255,255,255
        }

        // ---- [Combo] ---------------------------------------------------------------

        [Test]
        public void Combo_team_layouts() {
            var c1 = Fight.Combo[0];
            Assert.AreEqual(130, c1.Pos[0]); Assert.AreEqual(260, c1.Pos[1]);   // team1.pos = 130,260
            Assert.AreEqual(-240f, c1.StartX, 1e-6f);                            // team1.start.x = -240
            Assert.AreEqual("HITS!\\n%p%", c1.Text[0].Text);                     // team1.text.text
            Assert.AreEqual(7, c1.Text[0].FontIndex);
            Assert.AreEqual(6, c1.Counter[0].FontIndex);
            Assert.AreEqual(90, c1.DisplayTime);
            Assert.AreEqual(20, c1.CounterShake.Time);                           // team1.counter.shake.time = 20
            // team2.start.x = 1520 is rewritten by Go readFightScreenCombo: 1280 - 1520 = -240.
            var c2 = Fight.Combo[1];
            Assert.AreEqual(1150, c2.Pos[0]);
            Assert.AreEqual(-240f, c2.StartX, 1e-6f);
        }

        // ---- [Round] ---------------------------------------------------------------

        [Test]
        public void Round_default_single_and_final() {
            var r = Fight.Round;
            Assert.AreEqual(30, r.StartWaitTime);
            Assert.AreEqual(0, r.RoundTime);
            Assert.AreEqual("R   %i", r.RoundDefault.Text.Text);                // round.default.text
            Assert.AreEqual(9, r.RoundDefault.Text.FontIndex);                   // round.default.font = 9,0,0
            Assert.AreEqual(55, r.RoundDefault.DisplayTime);
            Assert.AreEqual(630f, r.RoundDefault.Text.Layout.OffsetX);           // round.default.offset = 630, 480
            Assert.AreEqual(480f, r.RoundDefault.Text.Layout.OffsetY);
            Assert.AreEqual(0, r.RoundDefault.Snd[0]); Assert.AreEqual(11, r.RoundDefault.Snd[1]);
            Assert.AreEqual(2, r.RoundDefault.Text.Layout.LayerNo, "round layer defaults to 2");
            Assert.AreEqual(1, r.Round[0].Snd[1]);                               // round1.snd = 0,1
            Assert.AreEqual(9, r.Round[8].Snd[1]);                               // round9.snd = 0,9
            Assert.AreEqual("G   r", r.RoundSingle.Text.Text);
            // round.final.text appears twice; the first value wins (Go IniSection.Parse).
            Assert.AreEqual("F   R", r.RoundFinal.Text.Text);
            Assert.AreEqual(520, r.Fight.Anim.AnimNo);                           // fight.anim = 520
            Assert.AreEqual(5, r.FightSndTime);
            Assert.AreEqual(50, r.CtrlTime);
            Assert.AreEqual(60, r.CallFightTime);
        }

        [Test]
        public void Round_ko_to_draw() {
            var r = Fight.Round;
            Assert.AreEqual(0, r.KoTime);
            Assert.AreEqual("", r.Ko.Text.Text, "KO has no text, only anim 529");
            Assert.AreEqual(-1, r.Ko.Text.FontIndex);
            Assert.AreEqual(529, r.Ko.Anim.AnimNo);
            Assert.AreEqual(540f, r.Ko.Anim.Layout.OffsetX);                     // KO.offset = 540, 400
            Assert.AreEqual(1, r.Ko.Anim.Layout.LayerNo, "KO layer defaults to 1");
            Assert.AreEqual(2, r.Ko.Snd[0]); Assert.AreEqual(0, r.Ko.Snd[1]);     // KO.snd = 2,0
            Assert.AreEqual(533, r.KoBg[0].AnimNo);                              // KO.bg0.anim = 533
            Assert.AreEqual(530, r.KoBg[3].AnimNo);
            Assert.AreEqual(120, r.Dko.DisplayTime);
            Assert.AreEqual(540, r.To.Anim.AnimNo);
            Assert.AreEqual(70, r.To.DisplayTime);
            Assert.AreEqual(544, r.Draw.Anim.AnimNo);
            Assert.AreEqual(80, r.Draw.DisplayTime);
            Assert.AreEqual(300, r.OverTime);                                    // over.time = 300
            Assert.AreEqual(0.25f, r.SlowSpeed, 1e-6f);
        }

        [Test]
        public void Round_win_messages() {
            var r = Fight.Round;
            Assert.AreEqual(60, r.WinTime);                                      // win.time = 60
            var p1 = r.Win[0].Text[0];
            Assert.AreEqual("%s", p1.Text.Text);                                 // p1.win.text = "%s"
            Assert.AreEqual(1, p1.Text.FontIndex);                               // p1.win.font = 1,0,1
            Assert.AreEqual(1, p1.Text.FontAlign);
            Assert.AreEqual(180, p1.DisplayTime);
            Assert.AreEqual(2, p1.Snd[0]); Assert.AreEqual(3, p1.Snd[1]);
            Assert.AreEqual(-1, r.Win[0].Text[1].Text.FontAlign);                // p2.win.font = 1,0,-1
            Assert.AreEqual("%s and %s", r.Win[1].Text[0].Text.Text);            // p1.win2.text
            Assert.AreEqual(586, r.Win[0].Bg[0][0].AnimNo);                      // p1.win.bg0.anim = 586
            Assert.AreEqual(583, r.AiWin[0].Text[0].Anim.AnimNo);                // ai.win.anim = 583
            Assert.IsTrue(r.AiWinExists[0][0]);
            var perfect = r.WinType[(int)FightWinType.Perfect];
            Assert.AreEqual(86, perfect.Time);                                   // p1.perfect.time = 86
            Assert.AreEqual(125, perfect.SndTime);
            Assert.AreEqual(550, perfect.Bg.AnimNo);
            Assert.AreEqual(1, perfect.Bg.Layout.LayerNo);
        }

        // ---- LifeBar.step (fightscreen.go) -----------------------------------------
        //
        // Scenario: 1000 max life, one idle tick, then 300 damage with get-hit held for 10
        // ticks. With fight.def's [Lifebar] (Go defaults freeze = 1, delay = 30, steps = 8):
        //   - TopLife (front) halves its gap each tick: tick 1 -> 0.85.
        //   - MidLife stays at 1.0 while hit and while mlifetime counts 30 -> 0: the counter
        //     starts decrementing on tick 11 and hits 0 on tick 40, the first tick mid moves:
        //     1 + (0.7 - 1) / 8 = 0.9625.
        //   - Then mid - 0.7 = 0.3 * (7/8)^k; it is under half a life point (0.0005) from
        //     k = 48, i.e. tick 40 + 47 = 87 (k = 47 gives 0.00056 at tick 86).

        static LifeBarState DamagedBar(out float[] top, out float[] mid) {
            var st = new LifeBarState(Fight.LifeBar[0], 1000, 3000);
            st.Step(false);
            st.Life = 700;
            top = new float[100];
            mid = new float[100];
            for (int t = 1; t < 100; t++) {
                st.Step(t <= 10);
                top[t] = st.TopLife;
                mid[t] = st.MidLife;
            }
            return st;
        }

        [Test]
        public void HealthBar_front_follows_damage_immediately() {
            float[] top, mid;
            DamagedBar(out top, out mid);
            Assert.AreEqual(0.85f, top[1], 1e-6f);
            Assert.AreEqual(0.775f, top[2], 1e-6f);
            Assert.AreEqual(0.7f, top[30], 1e-5f);
        }

        [Test]
        public void HealthBar_mid_waits_for_the_delay_then_catches_up() {
            float[] top, mid;
            var st = DamagedBar(out top, out mid);
            for (int t = 1; t <= 39; t++) Assert.AreEqual(1f, mid[t], "mid frozen at tick " + t);
            Assert.AreEqual(0.9625f, mid[40], 1e-5f);
            Assert.GreaterOrEqual(Math.Abs(mid[86] - 0.7f) * 1000f, 0.5f, "tick 86 still above");
            Assert.Less(Math.Abs(mid[87] - 0.7f) * 1000f, 0.5f, "tick 87 caught up");
            Assert.AreEqual(0.7f, st.MidLife, 1e-4f);
            Assert.AreEqual(0, st.MidLifeTime);
        }

        [Test]
        public void HealthBar_heal_snaps_front_up() {
            var st = new LifeBarState(Fight.LifeBar[0], 1000, 3000);
            st.Life = 500;
            st.Step(false);
            st.Life = 800;
            st.Step(false);
            // Go: toplife <= life -> toplife = life.
            Assert.AreEqual(0.8f, st.TopLife, 1e-6f);
        }
    }
}
