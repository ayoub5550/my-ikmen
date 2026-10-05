using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.Core;
using IK.UI;

namespace IK.Tests {
    /// <summary>
    /// The background layout maths of <see cref="StageRenderer"/>, checked against the real
    /// `assets/screenpack/stages/kfm.def` and against the Go formula in
    /// engine/ikemen-go/src/stage.go (`backGround.draw`), not against invented numbers.
    /// </summary>
    public class StageRenderTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(Repo, "assets", "screenpack", "stages");

        StageDefinition stage;

        [SetUp]
        public void Load() {
            stage = StageDefinition.Load(new FileSource(StageDir), "kfm.def");
        }

        [Test]
        public void Stage_sprites_are_loaded_from_the_sff() {
            Assert.IsNotNull(stage.Sprites, "kfm.sff must load with the stage");
            Assert.AreEqual("kfm.sff", stage.SpriteFile.ToLowerInvariant());
            Assert.IsNotNull(stage.Sprites.Get(0, 0), "the sky sprite 0,0 exists in kfm.sff");
            Assert.IsNotNull(stage.Sprites.Get(1, 0), "the wall sprite 1,0 exists in kfm.sff");
        }

        [Test]
        public void Element_x_follows_start_and_delta() {
            // [BG 0]: start = 0,0   delta = .5,.5
            var bg = stage.Backgrounds[0];
            Assert.AreEqual(0.5f, bg.Delta[0], 0.0001f);
            Assert.AreEqual(0f, StageRenderer.ElementX(bg, 0f), 0.0001f);
            // camera 100 units right moves a 0.5-delta layer 50 units left
            Assert.AreEqual(-50f, StageRenderer.ElementX(bg, 100f), 0.0001f);
        }

        [Test]
        public void Element_y_uses_start_y_and_the_bg_action_offset() {
            var floor = stage.Backgrounds.Find(b => b.Name.ToLowerInvariant().Contains("floor")
                                                    && b.Type == StageBgType.Parallax);
            Assert.IsNotNull(floor, "[BG Floor] exists in kfm.def");
            Assert.AreEqual(181f, floor.Start[1], 0.0001f);     // start = 0, 181
            Assert.AreEqual(181f, StageRenderer.ElementY(floor, 0f), 0.0001f);
            floor.Bga.Offset[1] = 7f;
            Assert.AreEqual(188f, StageRenderer.ElementY(floor, 0f), 0.0001f);
            floor.Bga.Offset[1] = 0f;
        }

        [Test]
        public void Tile_step_uses_the_resolved_spacing() {
            var wall = stage.Backgrounds.Find(b => b.Name == "Wall");
            Assert.IsNotNull(wall, "[BG Wall] exists in kfm.def");
            Assert.AreEqual(1, wall.Tile[0], "tile = 1, 0 in kfm.def");
            Assert.AreEqual(0, wall.Tile[1]);
            var spr = stage.Sprites.Get(wall.SpriteNo[0], wall.SpriteNo[1]);
            Assert.IsNotNull(spr);
            // tilespacing = 0,0 and the sprite is loaded, so Go resolves the step to the width
            Assert.AreEqual(spr.Width, StageRenderer.TileStep(wall, 0, spr.Width));
        }

        [Test]
        public void Tile_step_falls_back_to_the_sprite_size_when_unresolved() {
            var bg = new StageBackground();
            Assert.AreEqual(64, StageRenderer.TileStep(bg, 0, 64));
            bg.TileSpacingResolved[0] = 100;
            Assert.AreEqual(100, StageRenderer.TileStep(bg, 0, 64));
        }

        [Test]
        public void Tile_range_without_tiling_is_one_copy() {
            StageRenderer.TileRange(0, 64, 0f, -200f, 200f, 96, out int first, out int count);
            Assert.AreEqual(0, first);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Tile_range_covers_the_visible_width_when_tiling_is_infinite() {
            // a 100-wide tile at x = 0 covering -250..250 needs indices -3..3 => 7 copies
            StageRenderer.TileRange(1, 100, 0f, -250f, 250f, 96, out int first, out int count);
            Assert.AreEqual(-3, first);
            Assert.AreEqual(7, count);
            Assert.LessOrEqual(first * 100f, -250f);
            Assert.GreaterOrEqual((first + count - 1) * 100f, 250f);
        }

        [Test]
        public void Tile_range_shifts_with_the_element_position() {
            StageRenderer.TileRange(1, 100, 120f, -250f, 250f, 96, out int first, out int count);
            Assert.AreEqual(-4, first);                       // floor((-250-120)/100) = -4
            Assert.AreEqual(7, count);                        // up to ceil((250-120)/100) = 2
            Assert.LessOrEqual(120f + first * 100f, -250f);
            Assert.GreaterOrEqual(120f + (first + count - 1) * 100f, 250f);
        }

        [Test]
        public void Tile_range_respects_a_fixed_tile_count_and_the_cap() {
            StageRenderer.TileRange(3, 100, 0f, -250f, 250f, 96, out _, out int count);
            Assert.AreEqual(3, count, "tile = n > 1 draws n copies");
            StageRenderer.TileRange(1, 1, 0f, -5000f, 5000f, 96, out _, out int capped);
            Assert.AreEqual(96, capped, "the cap protects against a degenerate tilespacing");
        }

        [Test]
        public void Alpha_comes_from_the_trans_and_alpha_lines() {
            var reflection = stage.Backgrounds.Find(b => b.Name.ToLowerInvariant().Contains("reflection"));
            Assert.IsNotNull(reflection, "[BG Wall reflection on floor] exists in kfm.def");
            // trans = addalpha / alpha = 128,128
            Assert.AreEqual(TransType.Add, reflection.Trans);
            Assert.AreEqual(128, reflection.SrcAlpha);
            Assert.AreEqual(128f / 255f, StageRenderer.ElementAlpha(reflection), 0.001f);
            var wall = stage.Backgrounds.Find(b => b.Name == "Wall");
            Assert.AreEqual(1f, StageRenderer.ElementAlpha(wall), 0.0001f);
        }

        [Test]
        public void Parallax_elements_keep_their_two_x_scales() {
            var floor = stage.Backgrounds.Find(b => b.Name.ToLowerInvariant().Contains("floor")
                                                    && b.Type == StageBgType.Parallax);
            // xscale = 1, 1.75 in kfm.def: the far edge is narrower than the near edge
            Assert.AreEqual(1f, floor.XScale[0], 0.0001f);
            Assert.AreEqual(1.75f, floor.XScale[1], 0.0001f);
            var ceiling = stage.Backgrounds.Find(b => b.Name.ToLowerInvariant().Contains("ceiling"));
            Assert.IsNotNull(ceiling);
            Assert.AreEqual(1.425f, ceiling.XScale[0], 0.0001f);
            Assert.AreEqual(1f, ceiling.XScale[1], 0.0001f);
        }

        [Test]
        public void Layers_are_split_into_background_and_foreground() {
            foreach (var bg in stage.Backgrounds)
                Assert.That(bg.LayerNo, Is.InRange(0, 1), "kfm.def only uses layerno 0 and 1");
            Assert.AreEqual(0, stage.Backgrounds[0].LayerNo);
        }

        [Test]
        public void The_engine_ticks_the_stage_animations() {
            var charDir = Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");
            var src = new FileSource(charDir);
            var chr = MugenCharacter.Load(src, "kfm.def");
            var cns = CnsFile.Parse(src.Read(chr.CnsFile));
            var cmd = CmdFile.Parse(src.Read(chr.CmdFile));
            cns.Merge(cmd.States);
            var engine = new FightEngine(new Fighter(chr, cns, cmd), new Fighter(chr, cns, cmd),
                                         stage, null);
            int before = stage.StageTime;
            for (int i = 0; i < 10; i++) engine.Tick(CmdKey.None, CmdKey.None);
            Assert.AreEqual(before + 10, stage.StageTime, "ten engine ticks are ten stage ticks");
        }

        [Test]
        public void Stage_zoffset_places_the_floor() {
            // kfm.def: zoffset = 200 with a 320x240 localcoord, so the floor sits 40 units
            // above the bottom of the screen and the fighters stand on it
            Assert.AreEqual(200, stage.ZOffset);
            Assert.AreEqual(240, stage.LocalCoord[1]);
        }
    }
}
