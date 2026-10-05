using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.5 gate for the motif loader (Core/Motif.cs). Every expected value is quoted from
    /// assets/screenpack/data/ikemen1/system.def (section + key in the comments) or measured
    /// from system.sff with the independent Python decoder (tools/sff_dump.py).
    /// </summary>
    public class MotifTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string DataDir => Path.Combine(Repo, "assets", "screenpack", "data");
        static string MotifDir => Path.Combine(DataDir, "ikemen1");

        /// <summary>Ikemen's motif search path: the motif folder, data/, font/.</summary>
        static IResourceSource Source => new SearchPathSource(new FileSource(MotifDir), new FileSource(DataDir),
                                                              new FileSource(Path.Combine(Repo, "assets", "screenpack", "font")));

        static Motif parsed, loaded;
        static Motif M => parsed ?? (parsed = Motif.Parse(File.ReadAllBytes(Path.Combine(MotifDir, "system.def")), "system.def"));
        static Motif Loaded => loaded ?? (loaded = Motif.Load(Source, "system.def"));

        // ---- [Info] / [Files] ------------------------------------------------------

        [Test]
        public void Info_and_localcoord() {
            Assert.AreEqual("IKEMEN1", M.Name);                 // name = IKEMEN1
            Assert.AreEqual("Ohmga Shironeko", M.Author);       // author = Ohmga Shironeko
            Assert.AreEqual(1280, M.LocalCoord[0]);              // localcoord = 1280,720
            Assert.AreEqual(720, M.LocalCoord[1]);
        }

        [Test]
        public void Files_section() {
            Assert.AreEqual("system.sff", M.SprFile);
            Assert.AreEqual("system.snd", M.SndFile);
            Assert.AreEqual("select.def", M.SelectFile);
            Assert.AreEqual("fight.def", M.FightFile);
            Assert.AreEqual(9, M.Fonts.Count);                   // font1 .. font9
            Assert.AreEqual("ikemen1/fonts/Menu2.def", M.Fonts[3]);
            Assert.AreEqual("ikemen1/fonts/Menu1.def", M.Fonts[4]);
            Assert.AreEqual("ikemen1/fonts/Timer.def", M.Fonts[8]);
            Assert.AreEqual("ikemen1/fonts/PixelFlat.def", M.Fonts[9]);
            Assert.IsFalse(M.Fonts.ContainsKey(10));
        }

        [Test]
        public void Music_names() {
            Assert.AreEqual("sound/Title.mp3", M.Music["title.bgm"]);
            Assert.AreEqual("sound/Select.mp3", M.Music["select.bgm"]);
        }

        [Test]
        public void Every_begin_action_is_read() {
            // 23 [Begin Action n] blocks in system.def
            Assert.AreEqual(23, M.Animations.Actions.Count);
            Assert.IsNotNull(M.Animations.Get(160));             // P1 active cursor, 8 frames x 3 ticks
            Assert.AreEqual(8, M.Animations.Get(160).Frames.Count);
            Assert.AreEqual(3, M.Animations.Get(160).Frames[0].Time);
            Assert.IsNotNull(M.Animations.Get(900));             // continue counter
            Assert.IsNotNull(M.Animations.Get(200));             // VS logo
        }

        // ---- [Title Info] ------------------------------------------------------------

        [Test]
        public void Title_menu_layout() {
            var t = M.Title;
            Assert.AreEqual(10, t.FadeInTime);                   // fadein.time = 10
            Assert.AreEqual(1240f, t.MenuPos[0]);                // menu.pos = 1240,330
            Assert.AreEqual(330f, t.MenuPos[1]);
            Assert.AreEqual(4, t.ItemFont.FontIndex);            // menu.item.font = 4,0,-1
            Assert.AreEqual(-1, t.ItemFont.FontAlign);
            Assert.AreEqual(4, t.ActiveFont.FontIndex);          // menu.item.active.font = 4,0,-1, 123,206,255
            Assert.AreEqual(123, t.ActiveFont.Font[3]);
            Assert.AreEqual(206, t.ActiveFont.Font[4]);
            Assert.AreEqual(255, t.ActiveFont.Font[5]);
            Assert.AreEqual(54f, t.ItemSpacing[1]);              // menu.item.spacing = 0, 54
            Assert.AreEqual(6, t.VisibleItems);                  // menu.window.visibleitems = 6
            CollectionAssert.AreEqual(new[] { 100, 0 }, t.CursorMoveSnd);   // cursor.move.snd = 100,0
            CollectionAssert.AreEqual(new[] { 100, 1 }, t.CursorDoneSnd);
            CollectionAssert.AreEqual(new[] { 100, 2 }, t.CancelSnd);
        }

        [Test]
        public void Title_menu_item_names() {
            var t = M.Title;
            Assert.AreEqual("ARCADE", t.ItemName("menuarcade"));
            Assert.AreEqual("VS MODE", t.ItemName("menuversus"));
            Assert.AreEqual("TRAINING", t.ItemName("menupractice.training"));
            Assert.AreEqual("SURVIVAL", t.ItemName("menumission.survival"));
            Assert.AreEqual("WATCH MODE", t.ItemName("menuwatch"));
            Assert.AreEqual("OPTIONS", t.ItemName("options"));
            Assert.AreEqual("EXIT", t.ItemName("exit"));
            Assert.AreEqual("CREDITS", t.ItemName("credits", "CREDITS"));   // not in the motif: fallback
        }

        [Test]
        public void Title_background_has_every_element() {
            var bg = M.Background("TitleBG");
            Assert.IsNotNull(bg);
            // Sky, Clouds Top, Clouds Bottom, Black Bars, Black Menu Bars, LogoShadow x2, Logo, Logo 2
            Assert.AreEqual(9, bg.Backgrounds.Count);
            var logo = bg.Backgrounds[7];
            Assert.AreEqual("Title Logo", logo.Name);
            CollectionAssert.AreEqual(new[] { 0, 0 }, logo.SpriteNo);   // spriteno = 0, 0
            Assert.AreEqual(-250f, logo.Start[0]);                       // start = -250,155
            Assert.AreEqual(155f, logo.Start[1]);
            Assert.AreEqual(1, logo.LayerNo);
            Assert.AreEqual(15f, logo.SinRadius[1]);                     // sin.y = 15, 264, 2
            Assert.AreEqual(264, logo.SinLoopTime[1]);
            var clouds = bg.Backgrounds[1];
            Assert.AreEqual(-0.1f, clouds.Velocity[0], 1e-6);            // velocity = -0.1, 0
            Assert.AreEqual(1, clouds.Tile[0]);                          // tile = 1,0
            CollectionAssert.AreEqual(new[] { 255, 255, 255 }, bg.BgClearColor);   // bgclearcolor = 255,255,255
        }

        [Test]
        public void Background_blocks_of_every_screen() {
            Assert.AreEqual(6, M.Background("SelectBG").Backgrounds.Count);
            Assert.AreEqual(6, M.Background("VersusBG").Backgrounds.Count);
            Assert.AreEqual(6, M.Background("VictoryBG").Backgrounds.Count);
            Assert.AreEqual(1, M.Background("ContinueBG").Backgrounds.Count);   // [Continuebgdef] (lower case)
            Assert.AreEqual(3, M.Background("OptionBG").Backgrounds.Count);
            var vsLogo = M.Background("VersusBG").Backgrounds[4];
            Assert.AreEqual(StageBgType.Anim, vsLogo.Type);              // type = anim, actionno = 200
            Assert.AreEqual(200, vsLogo.ActionNo);
            Assert.IsNotNull(vsLogo.Animation, "the action comes from the motif's [Begin Action 200]");
            Assert.IsNull(M.Background("NoSuchBG"));
            Assert.AreEqual("", M.BackgroundText("NoSuchBG"));
        }

        [Test]
        public void Background_ticks_move_the_clouds() {
            var bg = M.Background("TitleBG");
            for (int i = 0; i < 10; i++) bg.Tick();
            // velocity -0.7 for Clouds Bottom: 10 ticks = -7 units
            Assert.AreEqual(-7f, bg.Backgrounds[2].Bga.Offset[0], 1e-3);
        }

        // ---- [Select Info] -------------------------------------------------------------

        [Test]
        public void Select_grid() {
            var s = M.Select;
            Assert.AreEqual(10, s.Rows);                         // rows = 10
            Assert.AreEqual(8, s.Columns);                       // columns = 8
            Assert.IsTrue(s.Wrapping);
            Assert.IsTrue(s.ShowEmptyBoxes);
            Assert.AreEqual(525f, s.Pos[0]);                     // pos = 525,195
            Assert.AreEqual(195f, s.Pos[1]);
            CollectionAssert.AreEqual(new[] { 24, 24 }, s.CellSize);
            Assert.AreEqual(4f, s.CellSpacing[0]);               // cell.spacing = 4 (both axes)
            Assert.AreEqual(4f, s.CellSpacing[1]);
            Assert.IsTrue(s.CellBg.HasSprite);
            Assert.AreEqual(150, s.CellBg.SprGroup);             // cell.bg.spr = 150,0
            Assert.AreEqual(0.25f, s.CellBg.Layout.ScaleX);      // cell.bg.scale = .25,.25
            Assert.AreEqual(151, s.CellRandom.SprGroup);         // cell.random.spr = 151,0
            Assert.AreEqual(4, s.RandomSwitchTime);
            Assert.AreEqual(9000, s.Portrait.SprGroup);          // portrait.spr = 9000,0
            Assert.AreEqual(0, s.Portrait.SprNumber);
            Assert.AreEqual(0.25f, s.Portrait.Layout.ScaleX);
            var c = s.CellPos(2, 1);
            Assert.AreEqual(525f + 2 * 28f, c[0]);
            Assert.AreEqual(195f + 28f, c[1]);
            Assert.AreEqual(3, s.PaletteSelect);                 // paletteselect = 3
        }

        [Test]
        public void Select_players() {
            var p1 = M.Select.P1;
            Assert.AreEqual(160, p1.CursorActive.AnimNo);        // p1.cursor.active.anim = 160
            Assert.AreEqual(161, p1.CursorDone.SprGroup);        // p1.cursor.done.spr = 161,0
            CollectionAssert.AreEqual(new[] { 100, 0 }, p1.CursorMoveSnd);
            Assert.AreEqual(0, p1.Face.AnimNo);                  // p1.face.anim = 0 (the character's stand)
            Assert.AreEqual(280f, p1.Face.Layout.OffsetX);       // p1.face.offset = 280, 560
            Assert.AreEqual(560f, p1.Face.Layout.OffsetY);
            Assert.AreEqual(0.75f, p1.Face.Layout.ScaleX);
            Assert.IsTrue(p1.Face.Layout.HasWindow);             // p1.face.window = 0,46, 476,629
            CollectionAssert.AreEqual(new[] { 0, 46, 476, 583 }, p1.Face.Layout.Window);
            Assert.AreEqual(3, p1.Name.FontIndex);               // p1.name.font = 3,0,1, 210,210,255
            Assert.AreEqual(1, p1.Name.FontAlign);
            Assert.AreEqual(50f, p1.Name.Layout.OffsetX);        // p1.name.offset = 50,620
            var p2 = M.Select.P2;
            Assert.AreEqual(170, p2.CursorActive.AnimNo);
            Assert.AreEqual(-1, p2.Face.Layout.Facing);          // p2.face.facing = -1
            Assert.AreEqual(1000f, p2.Face.Layout.OffsetX);
            Assert.AreEqual(-1, p2.Name.FontAlign);
            Assert.AreEqual(220f, p1.PalMenuPos[0]);             // p1.palmenu.pos = 220, 140
            Assert.AreEqual("Color", p1.PalText.Text);           // p1.palmenu.text.text = Color
        }

        [Test]
        public void Select_titles_and_stage() {
            var s = M.Select;
            Assert.AreEqual("ARCADE", s.TitleText("arcade"));
            Assert.AreEqual("VERSUS", s.TitleText("versus"));
            Assert.AreEqual("TRAINING", s.TitleText("training"));
            Assert.AreEqual("SURVIVAL", s.TitleText("survival"));
            Assert.AreEqual("WATCH MODE", s.TitleText("watch"));
            Assert.AreEqual(640f, s.Title.Layout.OffsetX);       // title.offset = 640,48
            Assert.AreEqual(640f, s.StagePos[0]);                // stage.pos = 640,707
            Assert.AreEqual(707f, s.StagePos[1]);
            Assert.AreEqual(3, s.StageActiveFont.FontIndex);
        }

        // ---- VS / victory / continue / win / survival ------------------------------------

        [Test]
        public void Versus_screen() {
            var v = M.Versus;
            Assert.AreEqual(390, v.Time);                        // time = 390
            Assert.AreEqual("Match %i", v.Match.Text);           // match.text = Match %i
            Assert.AreEqual(2, v.Match.FontIndex);
            Assert.AreEqual(1.5f, v.Match.Layout.ScaleX);
            Assert.AreEqual(0, v.P1.Portrait.AnimNo);            // p1.anim = 0
            Assert.AreEqual(444f, v.P1.Portrait.Layout.OffsetX); // p1.offset = 444, 540
            Assert.AreEqual(836f, v.P2.Portrait.Layout.OffsetX);
            Assert.AreEqual(-1, v.P2.Portrait.Layout.Facing);
            Assert.AreEqual(20f, v.P1.Name.Layout.OffsetX);      // p1.name.offset = 20,615
            Assert.AreEqual("Next Stage: %s", v.Stage.Text);
        }

        [Test]
        public void Victory_continue_win_survival() {
            var vic = M.Victory;
            Assert.IsTrue(vic.Enabled);
            Assert.AreEqual(360, vic.Time);
            Assert.AreEqual(9000, vic.P1Portrait.SprGroup);      // p1.spr = 9000,2
            Assert.AreEqual(2, vic.P1Portrait.SprNumber);
            Assert.AreEqual(75, vic.P2LoseBrightness);
            Assert.AreEqual("Winner!", vic.WinQuote.Text);

            var c = M.Continue;
            Assert.IsTrue(c.Enabled);
            Assert.AreEqual(640f, c.Pos[0]);                     // pos = 640,240
            Assert.AreEqual("CONTINUE?", c.Continue.Text);
            Assert.AreEqual(-80f, c.Yes.Layout.OffsetX);         // yes.offset = -80, 60
            Assert.AreEqual(80f, c.No.Layout.OffsetX);
            Assert.AreEqual(900, c.Counter.AnimNo);              // counter.anim = 900
            Assert.AreEqual(2000, c.CounterEndTime);
            Assert.AreEqual(291, c.CounterSkipTime[8]);          // counter.8.skiptime = 291
            CollectionAssert.AreEqual(new[] { 900, 8 }, c.CounterSnd[8]);
            Assert.AreEqual(9, c.DigitAt(0));
            Assert.AreEqual(9, c.DigitAt(290));
            Assert.AreEqual(8, c.DigitAt(291));
            Assert.AreEqual(0, c.DigitAt(1627));                 // counter.0.skiptime = 1627
            Assert.AreEqual(-1, c.DigitAt(2000));

            Assert.IsTrue(M.Win.Enabled);
            Assert.AreEqual("Congratulations!", M.Win.Text.Text);
            Assert.AreEqual(300, M.Win.ShowTime);                // pose.time = 300
            Assert.IsTrue(M.Survival.Enabled);
            Assert.AreEqual("Rounds survived: %i", M.Survival.Text.Text);
            Assert.AreEqual(5, M.Survival.RoundsToWin);
        }

        [Test]
        public void Infobox_text_section() {
            var lines = M.SectionLines("Infobox Text");
            Assert.AreEqual(7, lines.Count);
            StringAssert.StartsWith("Welcome to I.K.E.M.E.N GO engine!", lines[0]);
        }

        // ---- files through the search path ---------------------------------------------

        [Test]
        public void Load_reads_sprites_sounds_and_fonts() {
            var m = Loaded;
            Assert.IsNotNull(m.Sprites, "system.sff");
            Assert.AreEqual(296, m.Sprites.Sprites.Count);        // sff_dump: 296 sprites, 53 palettes
            var logo = m.Sprites.Get(0, 0);
            Assert.IsNotNull(logo);
            Assert.AreEqual(699, logo.Width);                    // sff_dump: 0,0 = 699x257 axis 349,128
            Assert.AreEqual(257, logo.Height);
            Assert.IsNotNull(m.Sprites.Get(150, 0));             // cell bg 106x106
            Assert.AreEqual(106, m.Sprites.Get(150, 0).Width);
            Assert.IsNotNull(m.Sounds, "system.snd");
            Assert.IsNotNull(m.Sounds.Get(100, 0), "cursor move sound 100,0");
            Assert.IsNotNull(m.Sounds.Get(100, 1), "cursor done sound 100,1");
            Assert.IsNotNull(m.Sounds.Get(900, 9), "continue counter voice 900,9");
            var bg = m.Background("TitleBG");
            Assert.AreSame(m.Sprites, bg.Sprites);
            // every sprite element of the title BG resolves in system.sff
            foreach (var e in bg.Backgrounds)
                Assert.IsNotNull(m.Sprites.Get(e.CurrentGroup, e.CurrentNumber), "sprite of " + e.Name);
        }

        [Test]
        public void Search_path_resolves_flattened_names() {
            var src = Source;
            Assert.IsNotNull(src.Read("ikemen1/fonts/Menu1.def"));     // data/ + path
            Assert.IsNotNull(src.Read("system.sff"));                  // motif folder
            Assert.IsNotNull(src.Read("f-4x6.def"));                   // font/
            Assert.IsNull(src.Read("nope.def"));
            Assert.AreEqual("Menu1.def", SearchPathSource.BareName("ikemen1/fonts/Menu1.def"));
        }
    }
}
