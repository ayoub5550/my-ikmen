using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.4 gate for the stage loader and the fighting camera. Every expected number is
    /// read from the stage .def files in assets/screenpack/stages (the line is quoted next to
    /// the assertion) or derived from the Go reference (engine/ikemen-go/src/stage.go,
    /// camera.go, anim.go); the derivation is written out in the comment.
    /// </summary>
    public class StageTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Stages => Path.Combine(Repo, "assets", "screenpack", "stages");

        static StageDefinition LoadStage(string def, bool sprites = false) {
            Assert.IsTrue(File.Exists(Path.Combine(Stages, def)), "missing stage " + def);
            return StageDefinition.Load(new FileSource(Stages), def, sprites);
        }

        // ---- kfm.def ---------------------------------------------------------------

        [Test]
        public void Kfm_info_stageinfo_and_player_starts() {
            var s = LoadStage("kfm.def");
            Assert.AreEqual("Mountainside Temple", s.Name);          // name = "Mountainside Temple"
            Assert.AreEqual("Mountainside Temple", s.DisplayName);   // displayname = "Mountainside Temple"
            Assert.AreEqual("Elecbyte", s.Author);                   // author = "Elecbyte"
            Assert.AreEqual("1.1", s.MugenVersionRaw);               // mugenversion = 1.1
            Assert.AreEqual(320, s.LocalCoord[0]);                   // localcoord = 320, 240
            Assert.AreEqual(240, s.LocalCoord[1]);
            Assert.AreEqual(200, s.ZOffset);                         // zoffset = 200
            Assert.AreEqual(-70, s.P1Start.StartX);                  // p1startx = -70
            Assert.AreEqual(0, s.P1Start.StartY);                    // p1starty = 0
            Assert.AreEqual(1, s.P1Start.Facing);                    // p1facing = 1
            Assert.AreEqual(70, s.P2Start.StartX);                   // p2startx = 70
            Assert.AreEqual(-1, s.P2Start.Facing);                   // p2facing = -1
            Assert.AreEqual(-1000f, s.LeftBound);                    // leftbound  = -1000
            Assert.AreEqual(1000f, s.RightBound);                    // rightbound =  1000
            Assert.AreEqual(15, s.ScreenLeft);                       // screenleft = 15
            Assert.AreEqual(15, s.ScreenRight);                      // screenright = 15
            Assert.AreEqual("kfm.sff", s.SpriteFile);                // spr = kfm.sff
            Assert.AreEqual("sound/kfm.mid", s.BgMusic);             // bgmusic = sound/kfm.mid
            Assert.AreEqual(100, s.BgmVolume);                       // bgmvolume = 100
        }

        [Test]
        public void Kfm_camera_section() {
            var s = LoadStage("kfm.def");
            CollectionAssert.AreEqual(new[] { -150, 150, -22, 0 }, s.CameraBounds);
            // boundleft = -150 / boundright = 150 / boundhigh = -22 / boundlow = 0
            Assert.AreEqual(0, s.StartX);                            // startx = 0
            Assert.AreEqual(0.2f, s.VerticalFollow, 1e-6f);          // verticalfollow = .2
            Assert.AreEqual(0, s.FloorTension);                      // floortension = 0
            Assert.AreEqual(60, s.Tension);                          // tension = 60
            Assert.AreEqual(35, s.CutHigh);                          // cuthigh = 35
            Assert.AreEqual(25, s.CutLow);                           // cutlow = 25
            Assert.AreEqual(1f, s.ZoomOut);                          // zoomout = 1
            Assert.AreEqual(1f, s.ZoomIn);                           // zoomin = 1
            Assert.IsFalse(s.YTensionEnable);                        // no tensionlow line (stage.go)
        }

        [Test]
        public void Kfm_shadow_and_reflection() {
            var s = LoadStage("kfm.def");
            Assert.AreEqual(64, s.Shadow.Intensity);                 // intensity = 64
            Assert.AreEqual(-0.1f, s.Shadow.YScale, 1e-6f);          // yscale = -.1
            Assert.AreEqual(-400, s.Shadow.FadeEnd);                 // fade.range = -400,-100
            Assert.AreEqual(-100, s.Shadow.FadeBegin);
            Assert.AreEqual(128, s.Reflection.Intensity);            // [Reflection] intensity = 128
        }

        [Test]
        public void Kfm_background_list() {
            var s = LoadStage("kfm.def");
            // [BG 0], [BG Floor], [BG Ceiling], [BG Wall reflection on floor], [BG Wall],
            // [BG Pillar Bottom], [BG Pillar Top]
            Assert.AreEqual(7, s.Backgrounds.Count);

            var first = s.Backgrounds[0];
            Assert.AreEqual("0", first.Name);
            Assert.AreEqual(StageBgType.Normal, first.Type);         // type  = normal
            CollectionAssert.AreEqual(new[] { 0, 0 }, first.SpriteNo);           // spriteno = 0, 0
            CollectionAssert.AreEqual(new[] { 0f, 0f }, first.Start);            // start = 0, 0
            CollectionAssert.AreEqual(new[] { 0.5f, 0.5f }, first.Delta);        // delta = .5,.5
            CollectionAssert.AreEqual(new[] { 0, 0 }, first.Tile);               // tile  = 0, 0
            Assert.AreEqual(0, first.LayerNo);                                   // layerno = 0
            Assert.AreEqual(-1, first.Mask);                         // mask = 0 -> Go anim.mask = -1

            var last = s.Backgrounds[6];
            Assert.AreEqual("Pillar Top", last.Name);
            Assert.AreEqual(StageBgType.Normal, last.Type);          // type  = normal
            CollectionAssert.AreEqual(new[] { 15, 1 }, last.SpriteNo);           // spriteno = 15,1
            CollectionAssert.AreEqual(new[] { 0f, 11f }, last.Start);            // start = 0, 11
            CollectionAssert.AreEqual(new[] { 0.8f, 0.75f }, last.Delta);        // delta = .8, .75
            CollectionAssert.AreEqual(new[] { 1, 0 }, last.Tile);                // tile = 1, 0
            Assert.AreEqual(0, last.Mask);                           // mask = 1 -> Go anim.mask = 0
        }

        [Test]
        public void Kfm_parallax_and_trans_elements() {
            var s = LoadStage("kfm.def");
            var floor = s.Backgrounds[1];
            Assert.AreEqual(StageBgType.Parallax, floor.Type);       // type  = parallax
            CollectionAssert.AreEqual(new[] { 10, 0 }, floor.SpriteNo);          // spriteno = 10,0
            CollectionAssert.AreEqual(new[] { 0f, 181f }, floor.Start);          // start = 0, 181
            CollectionAssert.AreEqual(new[] { 1f, 1.75f }, floor.XScale);        // xscale = 1, 1.75
            Assert.AreEqual(100f, floor.YScaleStart);                            // yscalestart = 100
            Assert.AreEqual(1.2f, floor.YScaleDelta, 1e-6f);                     // yscaledelta = 1.2

            var refl = s.Backgrounds[3];                             // [BG Wall reflection on floor]
            Assert.AreEqual(TransType.Add, refl.Trans);              // trans = addalpha (stage.go: TT_add)
            Assert.AreEqual(128, refl.SrcAlpha);                     // alpha = 128,128
            Assert.AreEqual(128, refl.DstAlpha);
            CollectionAssert.AreEqual(new[] { 0f, 239f }, refl.Start);           // start = 0, 239
        }

        // ---- stage0.def ------------------------------------------------------------

        [Test]
        public void Stage0_header_values() {
            var s = LoadStage("stage0.def");
            Assert.AreEqual("Training Room", s.Name);                // name = "Training Room"
            Assert.AreEqual(320, s.LocalCoord[0]);                   // localcoord = 320, 240
            Assert.AreEqual(240, s.LocalCoord[1]);
            CollectionAssert.AreEqual(new[] { -125, 125, -25, 0 }, s.CameraBounds);
            // boundleft = -125 / boundright = 125 / boundhigh = -25 / boundlow = 0
            Assert.AreEqual(50, s.Tension);                          // tension = 50
            Assert.AreEqual(190, s.ZOffset);                         // zoffset = 190
            Assert.AreEqual(-70, s.P1Start.StartX);                  // p1startx = -70
            Assert.AreEqual(70, s.P2Start.StartX);                   // p2startx = 70
            Assert.AreEqual(1, s.P1Start.Facing);                    // p1facing = 1
            Assert.AreEqual(-1, s.P2Start.Facing);                   // p2facing = -1
            Assert.AreEqual(96, s.Shadow.Intensity);                 // intensity = 96
            Assert.AreEqual(0.3f, s.Shadow.YScale, 1e-6f);           // yscale = .3
            Assert.AreEqual("", s.BgMusic);                          // bgmusic =
            Assert.AreEqual("stage0.sff", s.SpriteFile);             // spr = stage0.sff
        }

        [Test]
        public void Stage0_background_list() {
            var s = LoadStage("stage0.def");
            Assert.AreEqual(2, s.Backgrounds.Count);                 // [BG 0], [BG 1]
            var first = s.Backgrounds[0];
            Assert.AreEqual(StageBgType.Normal, first.Type);
            CollectionAssert.AreEqual(new[] { 0, 0 }, first.SpriteNo);           // spriteno = 0, 0
            CollectionAssert.AreEqual(new[] { 0f, 0f }, first.Start);            // start = 0, 0
            CollectionAssert.AreEqual(new[] { 1f, 1f }, first.Delta);            // delta = 1, 1
            CollectionAssert.AreEqual(new[] { 1, 0 }, first.Tile);               // tile  = 1, 0
            var last = s.Backgrounds[1];
            Assert.AreEqual(StageBgType.Normal, last.Type);
            CollectionAssert.AreEqual(new[] { 0, 1 }, last.SpriteNo);            // spriteno = 0, 1
            CollectionAssert.AreEqual(new[] { 0f, 185f }, last.Start);           // start = 0, 185
            CollectionAssert.AreEqual(new[] { 1f, 1f }, last.Delta);             // delta = 1, 1
            CollectionAssert.AreEqual(new[] { 1, 0 }, last.Tile);                // tile = 1, 0
            CollectionAssert.AreEqual(new[] { 0f, 0f }, last.Velocity);          // velocity = 0, 0
        }

        [Test]
        public void Stage0_loads_with_its_sprite_file() {
            var s = LoadStage("stage0.def", true);
            Assert.IsNotNull(s.Sprites, "stage0.sff should load");
            // every BG sprite named in the def exists in the SFF (spriteno = 0, 0 / 0, 1)
            foreach (var b in s.Backgrounds)
                Assert.IsNotNull(s.Sprites.Get(b.SpriteNo[0], b.SpriteNo[1]), "sprite of BG " + b.Name);
            // stage.go readBackGround: tilespacing += sprite size for sprite BGs
            var spr = s.Sprites.Get(0, 0);
            Assert.AreEqual(spr.Width, s.Backgrounds[0].TileSpacingResolved[0]);  // tilespacing = 0,0
        }

        [Test]
        public void Stage720_localcoord() {
            var s = LoadStage("stage0-720.def");
            Assert.AreEqual(1280, s.LocalCoord[0]);                  // localcoord = 1280, 720
            Assert.AreEqual(720, s.LocalCoord[1]);
            Assert.AreEqual(660, s.ZOffset);                         // zoffset = 660
            Assert.AreEqual(-280, s.P1Start.StartX);                 // p1startx = -280
            Assert.AreEqual(60, s.ScreenLeft);                       // screenleft = 60
            CollectionAssert.AreEqual(new[] { -500, 500, -450, 0 }, s.CameraBounds);
        }

        // ---- animated BG, BGCtrl ----------------------------------------------------

        [Test]
        public void Interactive_stage_anim_elements_and_bgctrl() {
            var s = LoadStage("interactivestage.def");
            Assert.AreEqual(16, s.Backgrounds.Count);                // 16 [BG ...] sections
            Assert.IsTrue(s.YTensionEnable);                         // tensionlow = 0 present
            Assert.AreEqual(120, s.TensionHigh);                     // tensionhigh = 120

            var col = s.Backgrounds[s.FindBackgrounds(101)[0]];      // [BG ColumnLeft] ID = 101
            Assert.AreEqual("ColumnLeft", col.Name);
            Assert.AreEqual(StageBgType.Anim, col.Type);             // type  = anim
            Assert.AreEqual(0, col.ActionNo);                        // actionno = 0
            Assert.IsNotNull(col.Animation);
            Assert.AreEqual(0, col.CurrentGroup);                    // [Begin Action 0] 0,1, 0,0, -1
            Assert.AreEqual(1, col.CurrentNumber);
            Assert.AreEqual(-1, col.Mask);                           // Go reads mask only for sprite BGs
            for (int i = 0; i < 100; i++) s.Tick();
            Assert.AreEqual(0, col.Animation.CurrentElement);        // time -1 never advances

            // [BGCtrlDef Left] CTRLID=201 + [BGCtrl ReflectionRight] type=Enable time=1,99999,-1 value=1 SCTRLID=301
            Assert.AreEqual(2, s.BgCtrlDefs.Count);
            Assert.AreEqual(2, s.BgCtrls.Count);
            var c = s.BgCtrls[0];
            Assert.AreEqual(StageBgCtrlType.Enable, c.Type);
            Assert.AreEqual(1, c.StartTime);
            Assert.AreEqual(99999, c.EndTime);
            Assert.AreEqual(-1, c.LoopTime);
            Assert.AreEqual(1, c.Value[0]);
            Assert.AreEqual(301, c.SCtrlId);
            Assert.AreEqual(1, c.Targets.Count);
            Assert.AreEqual("ColumnLeftReflection", s.Backgrounds[c.Targets[0]].Name);   // ID = 201
        }

        // Synthetic def: no stage in the screenpack has a multi-frame action, so the frame
        // timing comes from the AIR rules (anim.go Animation.Action): a 3-tick element hands
        // over on the 3rd tick, and after the last element the action loops to element 0.
        const string AnimatedDef =
            "[Info]\nname = \"Anim test\"\n[BGdef]\nspr = none.sff\n" +
            "[Begin Action 10]\n10,0, 0,0, 3\n10,1, 0,0, 3\n10,2, 0,0, 3\n" +
            "[BG Torch]\ntype = anim\nactionno = 10\nstart = 5, 6\nvelocity = 1, -0.5\n";

        [Test]
        public void Animated_bg_advances_its_element_and_moves_with_velocity() {
            var s = StageDefinition.Parse(AnimatedDef, "anim.def");
            Assert.AreEqual(1, s.Backgrounds.Count);
            var bg = s.Backgrounds[0];
            Assert.AreEqual(StageBgType.Anim, bg.Type);
            Assert.AreEqual(0, bg.Animation.CurrentElement);
            Assert.AreEqual(0, bg.CurrentNumber);
            for (int i = 0; i < 2; i++) bg.Tick();
            Assert.AreEqual(0, bg.Animation.CurrentElement, "still element 1 after 2 ticks");
            bg.Tick();
            Assert.AreEqual(1, bg.Animation.CurrentElement, "element 2 after 3 ticks");
            Assert.AreEqual(1, bg.CurrentNumber);
            for (int i = 0; i < 3; i++) bg.Tick();
            Assert.AreEqual(2, bg.CurrentNumber, "element 3 after 6 ticks");
            for (int i = 0; i < 3; i++) bg.Tick();
            Assert.AreEqual(0, bg.CurrentNumber, "looped back after 9 ticks");
            // bgAction.action: pos += vel every tick -> 9 * (1, -0.5)
            Assert.AreEqual(9f, bg.OffsetX, 1e-5f);
            Assert.AreEqual(-4.5f, bg.OffsetY, 1e-5f);
            // the stage start is untouched; reset puts everything back
            CollectionAssert.AreEqual(new[] { 5f, 6f }, bg.Start);
            bg.Reset();
            Assert.AreEqual(0, bg.Animation.CurrentElement);
            Assert.AreEqual(0f, bg.OffsetX);
        }

        [Test]
        public void Bg_animations_are_independent_copies() {
            // Go AnimationTable.get returns a copy, so two BGs on one action do not share state.
            string def = AnimatedDef + "[BG Torch2]\ntype = anim\nactionno = 10\n";
            var s = StageDefinition.Parse(def, "anim.def");
            Assert.AreEqual(2, s.Backgrounds.Count);
            s.Backgrounds[0].Tick(); s.Backgrounds[0].Tick(); s.Backgrounds[0].Tick();
            Assert.AreEqual(1, s.Backgrounds[0].Animation.CurrentElement);
            Assert.AreEqual(0, s.Backgrounds[1].Animation.CurrentElement);
        }

        // ---- camera (camera.go Camera.action, X part, zoom 1) ------------------------
        // stage0: boundleft = -125, boundright = 125, tension = 50, screenleft/right = 15,
        // screen width = localcoord 320 -> halfWidth 160, minLeft -285, maxRight 285.

        [Test]
        public void Camera_starts_at_startx_and_holds_for_start_positions() {
            var s = LoadStage("stage0.def");
            var cam = new StageCamera(s, s.LocalCoord[0]);
            Assert.AreEqual(0f, cam.X);                               // startx = 0
            // -70 / 70 are inside [-160+50, 160-50]: no tension push
            Assert.AreEqual(0f, cam.Update(s.P1Start.StartX, s.P2Start.StartX));
        }

        [Test]
        public void Camera_never_passes_boundleft() {
            var s = LoadStage("stage0.def");
            var cam = new StageCamera(s, s.LocalCoord[0]);
            // first update snaps: targetLeft = max(-300-50, -285) = -285 -> x = -285+160 = -125
            Assert.AreEqual(-125f, cam.Update(-300f, -200f));

            cam = new StageCamera(s, s.LocalCoord[0]);
            cam.Update(-70f, 70f);
            for (int i = 0; i < 600; i++) {
                float x = cam.Update(-300f, -280f);          // smoothed (tension) moves after the snap
                Assert.GreaterOrEqual(x, -125f, "tick " + i);
            }
            Assert.AreEqual(-125f, cam.X);
            Assert.AreEqual(-125f, cam.XBound(1f, -500f));   // XBound clamps to boundL
            Assert.AreEqual(125f, cam.XBound(1f, 500f));     // and boundR
        }

        [Test]
        public void Camera_follows_the_midpoint_of_the_players() {
            // Two players exactly (screen - 2*tension) apart sit on both tension lines, so the
            // camera centre is their midpoint: stage0 320-2*50 = 220 -> players -10 / 210.
            var s0 = LoadStage("stage0.def");
            var cam = new StageCamera(s0, s0.LocalCoord[0]);
            Assert.AreEqual(100f, cam.Update(-10f, 210f));
            // kfm: tension = 60 -> 320-2*60 = 200 -> players 0 / 200, midpoint 100
            var kfm = LoadStage("kfm.def");
            cam = new StageCamera(kfm, kfm.LocalCoord[0]);
            Assert.AreEqual(100f, cam.Update(0f, 200f));
        }

        [Test]
        public void Camera_respects_screenleft_and_screenright() {
            var s = LoadStage("stage0.def");
            var cam = new StageCamera(s, s.LocalCoord[0]);
            // players 400 apart do not fit: the zoom-out branch recentres the window (x = 0)
            Assert.AreEqual(0f, cam.Update(-200f, 200f));
            // system.go xmin/xmax: screen edge -/+ 160 with screenleft = 15 / screenright = 15
            Assert.AreEqual(-145f, cam.PlayerXMin);
            Assert.AreEqual(145f, cam.PlayerXMax);
            // camera parked on boundleft: the leftmost legal player x is -125-160+15
            cam = new StageCamera(s, s.LocalCoord[0]);
            cam.Update(-300f, -200f);
            Assert.AreEqual(-270f, cam.PlayerXMin);
            Assert.AreEqual(-125f + 160f - 15f, cam.PlayerXMax);
        }
    }
}
