using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.2 gate: every loader is compared against `tools/sff_dump.py`, an independent
    /// Python implementation of the same formats. The fixtures in
    /// `Assets/IK/Tests/EditMode/Fixtures/` are that script's output for the real KFM and
    /// screenpack files, so a regression in a decoder shows up as a changed sprite hash,
    /// not as a vaguely wrong picture.
    /// </summary>
    public class MugenLoaderTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");
        static string FixtureDir => Path.Combine(Application.dataPath, "IK", "Tests", "EditMode", "Fixtures");

        static string Sha1(byte[] data) {
            using (var sha = SHA1.Create()) {
                var h = sha.ComputeHash(data ?? new byte[0]);
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---- fixture plumbing ------------------------------------------------------

        [Serializable] class Fixture {
            public string file;
            public int[] version;
            public int sprites, palettes;
            public Entry[] entries;
            [Serializable] public class Entry {
                public int index, group, number, w, h, x, y, fmt, coldepth, palidx, bytes;
                public bool raw;
                public string sha1;
            }
        }

        static Fixture ReadFixture(string name) {
            var path = Path.Combine(FixtureDir, name);
            Assert.IsTrue(File.Exists(path), "missing fixture " + path);
            return JsonUtility.FromJson<Fixture>(File.ReadAllText(path));
        }

        static void CompareSff(string sffPath, string fixtureName, bool isCharacter) {
            Assert.IsTrue(File.Exists(sffPath), "missing sprite file " + sffPath);
            var fx = ReadFixture(fixtureName);
            var sff = SffFile.Load(File.ReadAllBytes(sffPath), isCharacter);

            Assert.AreEqual(fx.version[0], sff.VersionHigh, "SFF major version");
            Assert.AreEqual(fx.sprites, sff.Sprites.Count, "sprite count");
            Assert.AreEqual(fx.palettes, sff.Palettes.Count, "palette count");

            for (int i = 0; i < fx.entries.Length; i++) {
                var e = fx.entries[i];
                var s = sff.Sprites[i];
                string id = $"sprite #{i} ({e.group},{e.number})";
                Assert.AreEqual(e.group, s.Group, id + " group");
                Assert.AreEqual(e.number, s.Number, id + " number");
                Assert.AreEqual(e.w, s.Width, id + " width");
                Assert.AreEqual(e.h, s.Height, id + " height");
                Assert.AreEqual(e.x, (int)s.X, id + " x offset");
                Assert.AreEqual(e.y, (int)s.Y, id + " y offset");
                Assert.AreEqual(e.fmt, s.Format, id + " format");
                Assert.AreEqual(e.raw, s.Raw, id + " raw flag");
                Assert.AreEqual(e.sha1, Sha1(s.Pixels), id + " decoded pixels");
            }
        }

        // ---- SFF -------------------------------------------------------------------

        [Test]
        public void Sff_v2_lz5_and_png_sprites_match_the_python_reference() {
            CompareSff(Path.Combine(Chars, "kfm.sff"), "kfm_sff.json", true);
        }

        [Test]
        public void Sff_v1_pcx_sprites_match_the_python_reference() {
            CompareSff(Path.Combine(Chars, "intro.sff"), "kfm_intro_sff.json", true);
        }

        [Test]
        public void Sff_v2_rle8_sprites_match_the_python_reference() {
            CompareSff(Path.Combine(Repo, "assets/screenpack/font/arcade.sff"), "arcade_sff.json", false);
        }

        [Test]
        public void Sff_v2_png_sprites_match_the_python_reference() {
            CompareSff(Path.Combine(Repo, "assets/screenpack/data/fightfx.sff"), "fightfx_sff.json", false);
        }

        [Test]
        public void Every_decoder_matches_the_reference_on_random_streams() {
            var path = Path.Combine(FixtureDir, "decoder_vectors.json");
            Assert.IsTrue(File.Exists(path), "missing decoder vectors");
            var doc = JsonUtility.FromJson<VectorDoc>(File.ReadAllText(path));
            Assert.AreEqual(10, doc.vectors.Length);
            foreach (var v in doc.vectors) {
                var input = Convert.FromBase64String(v.input);
                var expected = Convert.FromBase64String(v.output);
                byte[] actual;
                switch (v.kind) {
                    case "rlepcx": actual = SffDecoders.RlePcx(input, v.w, v.h, v.bpl); break;
                    case "rle8": actual = SffDecoders.Rle8(input, v.w, v.h); break;
                    case "rle5": actual = SffDecoders.Rle5(input, v.w, v.h); break;
                    default: actual = SffDecoders.Lz5(input, v.w, v.h); break;
                }
                Assert.AreEqual(Sha1(expected), Sha1(actual), v.kind + " " + v.w + "x" + v.h);
            }
        }

        [Serializable] class VectorDoc { public Vector[] vectors; }
        [Serializable] class Vector { public string kind, input, output; public int w, h, bpl; }

        [Test]
        public void Sprites_can_be_looked_up_by_group_and_number() {
            var sff = SffFile.Load(File.ReadAllBytes(Path.Combine(Chars, "kfm.sff")));
            var stand = sff.Get(0, 0);
            Assert.IsNotNull(stand, "sprite 0,0 missing");
            Assert.AreEqual(47, stand.Width);
            Assert.AreEqual(106, stand.Height);
            Assert.AreEqual(18, (int)stand.X);
            Assert.AreEqual(105, (int)stand.Y);
            Assert.IsNull(sff.Get(31337, 1), "unknown sprite must be null, not an exception");
            var pal = sff.PaletteFor(stand);
            Assert.IsNotNull(pal);
            Assert.AreEqual(32, pal.Length, "KFM palettes hold 32 colours, not a padded 256");
            Assert.AreEqual(0u, pal[0], "palette index 0 is the transparent colour");
        }

        [Test]
        public void A_truncated_sff_is_rejected_with_a_clear_error() {
            var bytes = File.ReadAllBytes(Path.Combine(Chars, "kfm.sff"));
            var head = new byte[20];
            Array.Copy(bytes, head, 20);
            Assert.Throws<InvalidDataException>(() => SffFile.Load(head));
            var junk = new byte[64];
            Assert.Throws<InvalidDataException>(() => SffFile.Load(junk));
        }

        // ---- AIR -------------------------------------------------------------------

        [Test]
        public void Air_reads_every_action_of_kung_fu_man() {
            var air = AirFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.air")));
            Assert.AreEqual(117, air.Actions.Count, "KFM has 117 live actions (3 more are commented out)");
            Assert.AreEqual(0, air.Order[0]);
            Assert.AreEqual(5300, air.Order[air.Order.Count - 1]);

            var stand = air.Get(0);
            Assert.AreEqual(11, stand.Frames.Count);
            CollectionAssert.AreEqual(new[] { 10, 7, 7, 7, 7, 45, 7, 7, 7, 7, 40 },
                                      stand.Frames.ConvertAll(f => f.Time));
            Assert.AreEqual(151, stand.TotalTime, "sum of the element times");
            Assert.AreEqual(0, stand.LoopStart);
            foreach (var f in stand.Frames) {
                Assert.AreEqual(0, f.Group);
                Assert.AreEqual(2, f.Clsn2.Count, "Clsn2Default applies to every element");
                Assert.AreEqual(0, f.Clsn1.Count);
            }
            // Clsn2Default: 2 / Clsn2[0] = -13, 0, 16,-79 — normalised to l,t,r,b
            var box = stand.Frames[0].Clsn2[0];
            Assert.AreEqual(-13f, box[0]); Assert.AreEqual(-79f, box[1]);
            Assert.AreEqual(16f, box[2]); Assert.AreEqual(0f, box[3]);
        }

        [Test]
        public void Air_reads_the_flip_flag_of_the_turning_action() {
            var air = AirFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.air")));
            var turn = air.Get(5);
            Assert.AreEqual(2, turn.Frames.Count);
            Assert.AreEqual(-1, turn.Frames[0].Hscale, "first element has the H flag");
            Assert.AreEqual(1, turn.Frames[1].Hscale);
            Assert.AreEqual(4, turn.Frames[0].Time);
        }

        [Test]
        public void An_infinite_element_keeps_the_action_on_its_last_frame() {
            var air = AirFile.Parse("[Begin Action 7]\n0,0, 0,0, 5\n0,1, 0,0, -1\n");
            var a = air.Get(7);
            Assert.AreEqual(-1, a.TotalTime);
            a.Reset();
            for (int i = 0; i < 300; i++) a.Tick();
            Assert.AreEqual(1, a.CurrentElement, "an infinite element never advances");
            Assert.IsFalse(a.LoopEnd);
        }

        [Test]
        public void A_looping_action_returns_to_loopstart() {
            var air = AirFile.Parse("[Begin Action 1]\n0,0, 0,0, 2\nLoopstart\n0,1, 0,0, 3\n0,2, 0,0, 3\n");
            var a = air.Get(1);
            Assert.AreEqual(1, a.LoopStart);
            Assert.AreEqual(8, a.TotalTime);
            a.Reset();
            var seen = new List<int>();
            for (int i = 0; i < 16; i++) { seen.Add(a.CurrentElement); a.Tick(); }
            // 2 ticks on element 0, then 3 + 3 on elements 1 and 2, then back to element 1
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 1, 2, 2, 2, 1, 1, 1, 2, 2, 2, 1, 1 }, seen);
        }

        [Test]
        public void Element_transparency_and_scale_parameters_are_parsed() {
            var f = AirFile.ReadAnimFrame("100,3, -5, 7, 12, VH, AS128D64, 1.5, 0.5, 90");
            Assert.AreEqual(100, f.Group);
            Assert.AreEqual(3, f.Number);
            Assert.AreEqual(5, f.Xoffset, "H flips the x offset");
            Assert.AreEqual(-7, f.Yoffset, "V flips the y offset");
            Assert.AreEqual(12, f.Time);
            Assert.AreEqual(-1, f.Hscale);
            Assert.AreEqual(-1, f.Vscale);
            Assert.AreEqual(TransType.Add, f.Trans);
            Assert.AreEqual(128, f.SrcAlpha);
            Assert.AreEqual(64, f.DstAlpha);
            Assert.AreEqual(1.5f, f.Xscale, 0.0001f);
            Assert.AreEqual(0.5f, f.Yscale, 0.0001f);
            Assert.AreEqual(90f, f.Angle, 0.0001f);

            Assert.IsNull(AirFile.ReadAnimFrame("loopstart"), "a keyword line is not an element");
            Assert.IsNull(AirFile.ReadAnimFrame(""), "an empty line is not an element");
        }

        // ---- SND -------------------------------------------------------------------

        [Test]
        public void Snd_reads_every_wave_of_kung_fu_man() {
            var snd = SndFile.Load(File.ReadAllBytes(Path.Combine(Chars, "kfm.snd")));
            Assert.AreEqual(12, snd.Entries.Count);
            var first = snd.Get(0, 0);
            Assert.IsNotNull(first);
            Assert.AreEqual(1, first.Channels);
            Assert.AreEqual(8000, first.SampleRate);
            Assert.AreEqual(8, first.Bits);
            Assert.AreEqual(998, first.SampleCount);
            Assert.AreEqual(0.1247f, first.Duration, 0.001f);
            foreach (var e in snd.Entries) {
                Assert.IsNotNull(e.Samples, $"sound {e.Group},{e.Number} did not decode");
                foreach (var s in e.Samples) Assert.IsTrue(s >= -1.001f && s <= 1.001f, "sample out of range");
            }
        }

        // ---- DEF + character -------------------------------------------------------

        [Test]
        public void Def_reads_the_character_header_and_file_list() {
            var c = MugenCharacter.Load(new FileSource(Chars), "kfm.def");
            Assert.AreEqual("Kung Fu Man", c.Name);
            Assert.AreEqual("Kung Fu Man", c.DisplayName);
            Assert.AreEqual("Elecbyte", c.Author);
            Assert.AreEqual(320f, c.LocalCoordWidth);
            Assert.AreEqual(240f, c.LocalCoordHeight);
            Assert.AreEqual("kfm.sff", c.SpriteFile);
            Assert.AreEqual("kfm.air", c.AnimFile);
            Assert.AreEqual("kfm.snd", c.SoundFile);
            Assert.AreEqual(281, c.Sff.Sprites.Count);
            Assert.AreEqual(117, c.Air.Actions.Count);
            Assert.AreEqual(12, c.Snd.Entries.Count);
            Assert.IsEmpty(c.Warnings);
        }

        [Test]
        public void Every_playable_action_resolves_at_least_one_sprite() {
            var c = MugenCharacter.Load(new FileSource(Chars), "kfm.def");
            var playable = c.PlayableActions();
            Assert.GreaterOrEqual(playable.Count, 110, "almost every KFM action must be playable");
            foreach (var no in playable) {
                var a = c.Air.Get(no);
                bool any = false;
                foreach (var f in a.Frames) if (c.SpriteOf(f) != null) { any = true; break; }
                Assert.IsTrue(any, "action " + no + " has no sprite");
            }
        }

        [Test]
        public void Mugen_value_parsing_follows_the_engine_rules() {
            Assert.AreEqual(12, MugenDef.Atoi("12abc"));
            Assert.AreEqual(0, MugenDef.Atoi("abc"));
            Assert.AreEqual(-7, MugenDef.Atoi(" -7 "));
            Assert.AreEqual(0, MugenDef.Atoi(""));
            Assert.AreEqual(1.5f, MugenDef.Atof("1.5x"), 0.0001f);
            Assert.AreEqual(-0.25f, MugenDef.Atof("-.25"), 0.0001f);
            Assert.AreEqual(0f, MugenDef.Atof("x"), 0.0001f);
            Assert.IsTrue(MugenDef.IsNumeric(" 3.5 "));
            Assert.IsFalse(MugenDef.IsNumeric("3.5a"));
            Assert.AreEqual("value", MugenDef.Unquote("\"value\""));
            Assert.AreEqual("a = b", MugenDef.StripComment("a = b ; comment").Trim());
        }

        [Test]
        public void The_shipped_character_in_resources_matches_the_repository_copy() {
            var resources = Path.Combine(Application.dataPath, "IK", "Resources", "chars", "kfm");
            foreach (var name in new[] { "kfm.def", "kfm.sff", "kfm.air", "kfm.snd", "kfm.cmd", "kfm.cns" }) {
                var shipped = Path.Combine(resources, ResourcesSource.AssetName(name) + ".bytes");
                Assert.IsTrue(File.Exists(shipped), "missing shipped copy " + shipped);
                Assert.AreEqual(Sha1(File.ReadAllBytes(Path.Combine(Chars, name))),
                                Sha1(File.ReadAllBytes(shipped)),
                                name + " in Resources is stale — re-copy it from assets/");
            }
        }
    }
}
