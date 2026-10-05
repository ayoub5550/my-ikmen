using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.App;
using IK.Core;
using IK.Input;
using IK.Settings;

namespace IK.Tests {
    /// <summary>
    /// dev.7 gate: the allocation-free paths, frame pacing, the texture cache and the new
    /// video / control settings (docs/DEV7.md §2). Numbers that need a frame loop (FPS,
    /// allocations per frame) are measured by the benchmark, not here.
    /// </summary>
    public class Dev7PerfTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");

        [Test]
        public void Lower_CachesAndKeepsTheSameInstance() {
            string name = new string("StateNo".ToCharArray());
            string a = Lower.Of(name), b = Lower.Of(name);
            Assert.AreEqual("stateno", a);
            Assert.AreSame(a, b, "the second call must not allocate a new string");
            Assert.AreSame(Lower.Of(new string("STATENO".ToCharArray())), Lower.Of(new string("STATENO".ToCharArray())));
            Assert.IsNull(Lower.Of(null));
            Assert.AreEqual("command", "Command".Lc());
        }

        [Test]
        public void SnapDelta_RemovesJitterAroundWholeFrames() {
            Assert.AreEqual(1f / 60f, InputRouter.SnapDelta(0.0163f, 60f), 1e-7f);
            Assert.AreEqual(1f / 60f, InputRouter.SnapDelta(0.0181f, 60f), 1e-7f);
            Assert.AreEqual(2f / 60f, InputRouter.SnapDelta(0.0342f, 60f), 1e-7f, "30 FPS cap: two ticks a frame");
            Assert.AreEqual(1f / 120f, InputRouter.SnapDelta(0.0086f, 60f), 1e-7f, "120 Hz panel");
            Assert.AreEqual(0.0125f, InputRouter.SnapDelta(0.0125f, 60f), 1e-7f, "a real hitch is kept");
            Assert.AreEqual(0.25f, InputRouter.SnapDelta(3f, 60f), 1e-7f, "clamped after a stall");
            Assert.AreEqual(0f, InputRouter.SnapDelta(-1f, 60f));
        }

        [Test]
        public void Bleed_GivesTransparentTexelsTheirNeighboursColour() {
            // 3x1: red opaque | transparent | transparent-isolated far away handled below
            var px = new[] { new Color32(200, 0, 0, 255), new Color32(0, 0, 0, 0), new Color32(0, 0, 255, 255) };
            MugenAssetCache.Bleed(px, 3, 1);
            Assert.AreEqual(0, px[1].a, "alpha stays 0");
            Assert.AreEqual(100, px[1].r); Assert.AreEqual(127, px[1].b, "average of the opaque neighbours");
            var lone = new[] { new Color32(9, 9, 9, 0) };
            MugenAssetCache.Bleed(lone, 1, 1);
            Assert.AreEqual(new Color32(0, 0, 0, 0), lone[0]);
        }

        [Test]
        public void Cache_ReusesTexturesAndSprites() {
            var chr = MugenCharacter.Load(new FileSource(Chars), "kfm.def", loadSound: false);
            var spr = chr.Sff.Get(0, 0);
            using (var cache = new MugenAssetCache()) {
                int before = MugenAssetCache.TexturesCreated;
                var t1 = cache.CachedTextureFor(spr, chr.Sff.PaletteFor(spr));
                var t2 = cache.CachedTextureFor(spr, chr.Sff.PaletteFor(spr));
                Assert.AreSame(t1, t2, "parallax used to create a texture every frame");
                var s1 = cache.SpriteFor(chr.Sff, spr);
                Assert.AreSame(s1, cache.SpriteFor(chr.Sff, spr));
                Assert.AreSame(t1, s1.texture, "sprite and parallax share one texture");
                Assert.AreEqual(1, MugenAssetCache.TexturesCreated - before);
                Assert.IsFalse(t1.isReadable, "the CPU copy is released after upload");
            }
        }

        [Test]
        public void Prewarm_BuildsEveryAnimationFrameOnce() {
            var chr = MugenCharacter.Load(new FileSource(Chars), "kfm.def", loadSound: false);
            var frames = new HashSet<long>();
            foreach (var a in chr.Air.Actions.Values)
                foreach (var f in a.Frames) if (f.Group >= 0) frames.Add(((long)f.Group << 16) | (uint)f.Number);
            using (var cache = new MugenAssetCache()) {
                int made = cache.Prewarm(chr.Sff, frames, chr.Sff.Palettes[0]);
                Assert.Greater(made, 100, "KFM shows a few hundred sprites");
                Assert.AreEqual(0, cache.Prewarm(chr.Sff, frames, chr.Sff.Palettes[0]), "second time: all cached");
            }
        }

        [Test]
        public void Filter_FollowsTheSetting() {
            try {
                RenderQuality.ApplyFilter(PixelFilter.Smooth);
                Assert.AreEqual(FilterMode.Bilinear, MugenAssetCache.Filter);
                RenderQuality.ApplyFilter(PixelFilter.Crisp);
                Assert.AreEqual(FilterMode.Bilinear, MugenAssetCache.Filter, "crisp = bilinear + shader");
                Assert.AreEqual(1f, Shader.GetGlobalFloat("_IKPixelAA"));
                RenderQuality.ApplyFilter(PixelFilter.Sharp);
                Assert.AreEqual(FilterMode.Point, MugenAssetCache.Filter);
                Assert.AreEqual(0f, Shader.GetGlobalFloat("_IKPixelAA"));
            } finally { RenderQuality.ApplyFilter(PixelFilter.Sharp); }
        }

        [Test]
        public void RenderScale_KeepsTheAspect() {
            var s = RenderQuality.ScaledSize(new Vector2Int(2400, 1080), 70);
            Assert.AreEqual(new Vector2Int(1680, 756), s);
            Assert.AreEqual(new Vector2Int(2400, 1080), RenderQuality.ScaledSize(new Vector2Int(2400, 1080), 100));
            Assert.AreEqual(new Vector2Int(1200, 540), RenderQuality.ScaledSize(new Vector2Int(2400, 1080), 10), "clamped to 50 %");
        }

        [Test]
        public void Settings_NewFieldsClampAndDefault() {
            var s = new GameSettings();
            Assert.AreEqual(0, s.buttonStyle, "modern controls by default");
            s.buttonStyle = 7; s.pixelFilter = (PixelFilter)9;
            s.Clamp();
            Assert.AreEqual(1, s.buttonStyle);
            Assert.AreEqual(PixelFilter.Sharp, s.pixelFilter);
            s.pixelFilter = PixelFilter.Crisp; s.Clamp();
            Assert.AreEqual(PixelFilter.Crisp, s.pixelFilter);
        }

        [Test]
        public void DefaultLayout_StartAndPauseStayClearOfTheTimerAndEachOther() {
            var l = ControlLayout.Default();
            var safe = new Rect(0, 0, ControlLayout.RefWidth, ControlLayout.RefHeight);
            var start = l.RectOf(l.Get(ControlId.Start), safe, 1f);
            var pause = l.RectOf(l.Get(ControlId.Pause), safe, 1f);
            var timer = new Rect(ControlLayout.RefWidth / 2 - 120, ControlLayout.RefHeight - 150, 240, 150);
            Assert.IsFalse(start.Overlaps(timer), "START used to sit on the round timer");
            Assert.IsFalse(pause.Overlaps(timer));
            var startWide = new Rect(start.center.x - start.width * 0.8f, start.y, start.width * 1.6f, start.height);
            Assert.IsFalse(startWide.Overlaps(pause), "the START capsule is 1.6x wide");
            Assert.LessOrEqual(start.yMax, ControlLayout.RefHeight - 130, "below the motif lifebars");
        }

        [Test]
        public void DrawList_IntoAListMatchesTheAllocatingVersion() {
            var chr = MugenCharacter.Load(new FileSource(Chars), "kfm.def", loadSound: false);
            var cmd = CmdFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cmd")));
            var states = CnsFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cns")));
            states.Merge(cmd.States);
            var e = new FightEngine(new Fighter(chr, states, cmd, () => 500f), new Fighter(chr, states, cmd, () => 500f));
            for (int i = 0; i < 120; i++) e.Tick(CmdKey.x, CmdKey.None);
            var a = e.DrawList();
            var reuse = new List<object> { "stale" };
            var b = e.DrawList(reuse);
            Assert.AreSame(reuse, b);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Benchmark_SummaryPercentiles() {
            var frames = new List<PerfMonitor.Frame>();
            for (int i = 0; i < 99; i++) frames.Add(new PerfMonitor.Frame { Ms = 10f, LogicMs = 1f, DrawMs = 0.5f });
            frames.Add(new PerfMonitor.Frame { Ms = 60f, LogicMs = 2f, DrawMs = 4f });
            var r = Benchmark.Summarise(frames);
            Assert.AreEqual(100, r.frames);
            Assert.AreEqual(10.5f, r.avgMs, 1e-3f);
            Assert.AreEqual(10f, r.p50Ms); Assert.AreEqual(10f, r.p95Ms); Assert.AreEqual(10f, r.p99Ms);
            Assert.AreEqual(60f, r.maxMs);
            Assert.AreEqual(1000f / 60f, r.low1Fps, 1e-3f, "1 % low = the slowest frame here");
            Assert.AreEqual(1, r.hitches25); Assert.AreEqual(1, r.hitches50);
            StringAssert.Contains("FPS", Benchmark.Describe(r));
        }
    }
}
