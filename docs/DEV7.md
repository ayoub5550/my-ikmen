# dev.7 — frame rate, quality, performance and the on-screen buttons

Owner request (2026-10-05): «ابدأ في dev.7 وركز على تحسين الفريمات وجودة واداء افضل» + «وشكل الأزرار».
Branch `dev.7` from `main` 6f9abb6 (dev.6 merged as PR #7). Engine behaviour is unchanged:
27 seeded harness matches (3 pairs × 3 seeds × single / turns / tag) print the same trace on
`main` and on `dev.7`, tick for tick.

## 1. What changed

| # | Area | Change | Where |
| --- | --- | --- | --- |
| 1 | Measuring | `PerfMonitor`: per-frame wall time, logic / draw ms, managed allocations, CPU main / render / GPU times (`FrameTimingManager`, enabled in the player). FPS overlay = Options → Video → Show FPS (did nothing before). | `App/PerfMonitor.cs` |
| 2 | Measuring | Benchmark: Options → Video → Benchmark plays a fixed CPU-vs-CPU match (KFM 720 vs KFM ZSS, stage0-720, AI 8, no timer), one tick per frame, uncapped, 1800 frames; shows FPS / 1 % low / p95 / p99 / hitches / GC / memory and saves `benchmark.json`. Same code from the batch editor (`IK.EditorTools.IKBench.Run`) and the Linux test player (`-ikbench out.json`). | `App/Benchmark.cs`, `Editor/IKBench.cs` |
| 3 | Bug: leak | The parallax background built a **new texture every frame** (`StageRenderer.DrawParallax` → `TextureFor`). On stage0-720: 4,000+ textures and ~3.6 MB garbage per frame after a minute. Textures are now cached per (sprite, palette). | `UI/StageRenderer.cs`, `Core/ResourceSources.cs` |
| 4 | Bug: leak | The previous stage's textures were never released (one full set per match). | `UI/FightScreen.cs` `Load` |
| 5 | Garbage | `string.ToLowerInvariant` allocates on Mono / IL2CPP even for lower-case text; trigger and controller names were lower-cased on every evaluation (~45 KB per tick, state -1 alone). `Lower.Lc()` caches it: engine garbage 46.5 KB → 1.5 KB per tick, GCs in a full match 57 → 9 (harness `IK_PERF=1`). | `Core/Lower.cs`, `Fighter*.cs`, `Expr.cs`, `HitDef.cs` |
| 6 | Garbage | Draw list, afterimage loop, per-tick lists, `CmdKey[]`, sprite cache keys (string → long) are allocation free. | `FightEngineEx.DrawList(list)`, `FightScreen` |
| 7 | Draw cost | A sprite is only moved in the hierarchy when its draw order changes (`SetAsLastSibling` on every sprite every tick re-sorted the canvas); material properties use cached ids and are only set when they change; the stage + sprites live in a nested canvas, apart from the HUD. | `FightScreen.DrawSprite` |
| 8 | Hitches | Sprite prewarm: every animation frame of every fighter (with its palette) and the common hit sparks are built at load (250 ms slice) and then 3 ms per tick during the round intro, so the first fireball / super no longer stalls a frame on texture creation. | `FightScreen.Prewarm`, `MugenAssetCache.PrewarmStep` |
| 9 | Memory | The CPU copy of every sprite texture is released after upload (`Apply(false, true)`). | `MugenAssetCache.TextureFor` |
| 10 | Frame pacing | Frame times within 2 ms of 1/60 (or 2/60, 1/120) are snapped, so jitter no longer runs 0 or 2 logic ticks in a frame (visible stutter). | `InputRouter.SnapDelta` |
| 11 | Video settings | Render scale now works (back buffer `Screen.SetResolution` on the phone); pixel filter now works: Sharp (point), Smooth (bilinear) and new **Crisp** (pixel-art anti-aliasing in IK/UIPalFx: square pixels of even width at non-integer scales). Transparent texels get their neighbours' colour so filtering has no dark fringes. | `App/RenderQuality.cs`, `UIPalFx.shader` |
| 12 | Build | IL2CPP Master configuration + OptimizeSpeed code generation. | `IKBuildPipeline.ConfigurePlayerSettings` |
| 13 | Buttons | New **modern** look (default; Options → Controls → Button style → Classic keeps dev.1-6): dark glass face, coloured anti-aliased ring per button (punches warm, kicks cool), bold Rubik labels, pressed = filled + glow + 7 % smaller; round stick base whose arrows light up with the held direction; START capsule. | `Input/ButtonLook.cs`, `UI/Skin.cs`, `DirectionPad.cs`, `TouchControls.cs` |
| 14 | **Bug: invisible controls** | The touch canvas (order 5) was drawn *under* the UI canvas (order 10), whose fight / training panels are opaque: in a match the buttons worked but were **not visible**. The touch canvas is now order 20; the controls hide under the pause menu; the direction zone leaves the top 22 % free for HUD buttons. New rendered checks compare pixels with and without the controls. | `IKApp`, `FightScreen.TogglePause`, `DirectionPad` |
| 15 | Bug | The on-screen pause button (`m`) did nothing in a match; it now pauses. The HUD "Back" and centre "II" plates (which covered the motif lifebar / timer) only show in training / when touch is off. START / II moved below the lifebars (top centre covered the round timer). | `FightScreen`, `ControlLayout.Default` |
| 16 | Tooling | Linux test player (`IKBuildPipeline.BuildLinux`): runs under xvfb at any resolution, `-ikshots dir` saves real 2400×1080 screenshots of the controls and filters (the batch editor's game view is always 640×480). | `App/ShowcaseShots.cs` |

## 2. Evidence

- **EditMode: 274/274** (dev.6: 263; new `Dev7PerfTests`, 11 cases: lower-case cache, frame snap,
  bleed, texture / sprite cache reuse, prewarm, filters, render scale, settings, default layout,
  draw list, benchmark statistics).
- **Rendered UI: 177/177** (dev.6: 175; new: attack buttons and direction pad really drawn on top of
  the training screen). As TESTING.md §3 says, the batch game view is 640×480 whatever the
  `-screen` argument, so the three runs are the same size; the 2400×1080 look was checked with the
  Linux player screenshots.
- **Engine parity:** 27/27 harness traces identical between `main` and `dev.7`.
- **Benchmark, Linux player, 2400×1080, 3600 frames** (llvmpipe = software GPU, so FPS is GPU
  bound here; compare the CPU columns). "dev.6 path" = this branch with dev.6's FightScreen /
  StageRenderer / engine files:

| | dev.6 path | dev.7 |
| --- | --- | --- |
| Draw (fight code) per frame | 15.41 ms | **0.20 ms** |
| CPU main thread per frame | 17.1 ms | **1.5 ms** |
| 1 % low FPS | 38 | **55** |
| Frames > 25 ms | 11 | **1** |
| Fight garbage per frame | 3,657 KB | **~9 KB** |
| GC collections in 60 s | 728 | **16** |
| Textures alive at the end | 4,073 (growing) | **892 (stable)** |
| Load | 1.0 s | 1.25 s (+prewarm slice) |

  Crisp filter on the software rasteriser: +6 ms per frame (fragment bound); on a phone GPU
  expected to be small but **not measured** — Sharp stays the default. Run Options → Video →
  Benchmark on the device for real numbers.
- **APK:** `my-ikmen.apk` (release `v0.1.0-dev.7`), 40,724,266 bytes, sha256 `37e022dbc5acf40ed528dabf65598c703810cf1bf85e5ec9b056cdb2da7bd7df`; com.ayoub.ikmen 0.1.0-dev.7 (code 7), arm64-v8a, IL2CPP Master, debug-signed (apksigner verify OK).
- **Not run:** Firebase Test Lab (no service account) and the owner's Poco F3.

## 3. Known gaps

- No phone numbers yet: every figure above is from the sandbox. The in-game benchmark exists so
  the owner can report real FPS / 1 % low on the Poco F3.
- Sprites are RGBA per palette (memory ×4 compared with an 8-bit index texture + palette shader).
  A palette shader would also make palette swaps free; deferred (touches PalFX).
- The HUD (`FightHud`) still rebuilds its text every tick it changes; not a hotspot in the
  benchmark.
- Carried over from dev.6: Simul, tag run-in states, graphic team HUD, own content replacing KFM
  and the Elecbyte fonts, owner keystore signing, AAB.

## 4. Next

| Wave | Contents |
| --- | --- |
| dev.8 | Poco F3 results from the benchmark → targeted fixes; palette-index textures; own characters / fonts; keystore + AAB; Simul, tag run-in, team HUD |
