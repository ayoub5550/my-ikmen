# dev.8 — roadmap (device bugs, engine accuracy, content)

Owner report on dev.7 (2026-10-05, real phone):
«التنقل في الإعدادات · الخلفية لا توجد · مشاكل الاهتزاز · اللاعب يعلق كثيرا · لم تترجم الكود جيدا فيها آلاف الأخطاء · صغيرة في الحجم»
and «اقصد حجم اللعبة الأصلية كاملة فيها المفروض محتوى أكثر من هيك».

Branch `dev.8` from `dev.7` b02cb65 (PR #8 still open). This first commit adds only the
**device probe** (measuring tool) and this plan; no game behaviour changes yet.

## Done in dev.8 (wave A + the "stuck player" + own content)

| # | Owner symptom | Change | Where |
| --- | --- | --- | --- |
| 1 | **Player gets stuck** | Root cause found: the ZSS compiler flattened `if / else if / else` into per-controller conditions, so every controller **re-evaluated** the condition text. With `random` in it (kfm_zss AI: `if power > 450 && random%3 = 0 { let spver = 1020 } else if … { let spver = 1010 } else { let spver = 1000 }` then `changeState{value: $spver}`), each branch rolled its own random: sometimes none fired, `$spver` stayed 0 and the fighter went to **state 0 without control** for 1–2 s. Conditions (and `switch` values) are now evaluated once, where they stand, into hidden locals, like Ikemen's compiled blocks. Harness detector `IK_CTRL0=1` (state 0 + ctrl 0 for ≥ 20 ticks mid-round): **13 events in 20 matches on dev.7 → 0 on dev.8**. | `Core/ZssFile.cs`, `tools/harness/Main.cs` |
| 2 | **No background** | SFF v2 sprites can be decoded lazily (`SffFile.Load(lazy: true)`): the 9 MB `system.sff` is only indexed at start and the title decodes the ~10 sprites it draws, so the sky / logo are there on the first frame instead of after ~6 s of black. Sounds still decode on a worker thread. A failed sprite decode is recorded (`SffFile.LastDecodeError`, logged by the probe). | `Core/SffFile.cs`, `UI/MotifAssets.cs`, `UI/FrontEndScreen.cs` |
| 3 | No background on 20:9 | The motif background layers are drawn in rects scaled to **cover** the screen (no black bars on 2400×1080); menus and texts stay in the height-fitted motif area, so nothing interactive is cropped. | `UI/MotifView.cs`, `UI/MotifCoverFit.cs` |
| 4 | **Settings navigation** | Every page scrolls (drag / fling, clamped), the panel is opaque, Arabic rows are right-to-left (label right, controls left), multi-line Arabic text keeps its line order (the About page overlapped). | `UI/SettingsMenu.cs`, `UI/ArabicShaper.cs` |
| 5 | Title | Arabic menu lines no longer overlap (TrueType height capped to the item spacing); the footer shows the real version (`Application.version`) instead of "dev.6". | `UI/TitleScreen.cs`, `UI/MotifView.cs` |
| 6 | **"Small" — content** | Own content on the phone: copy MUGEN / Ikemen characters to `Android/data/com.ayoub.ikmen/files/chars/<name>/` and stages to `files/stages/`; they join the roster after the built-in ones (or put your own `files/data/select.def` to replace it). File names are matched case-insensitively with `\` or `/`. The folders and a README are created on first start. The select grid grows rows and shrinks cells when there are more characters than motif cells. | `Core/ResourceSources.cs` (`ExternalSource`, `ContentSource`), `UI/MotifAssets.cs`, `UI/FightScreen.cs`, `UI/SelectScreen.cs`, `App/IKApp.cs` |
| 7 | Measuring | `DeviceProbe` (probe builds / `-ikprobe` / TEST_LOOP): device info, title timing, screenshots of title and settings, two watched matches with per-second state / ctrl / life, benchmark → `files/probe/probe.log`. | `App/DeviceProbe.cs`, `Editor/IKGameLoopManifest.cs` |

Not done yet (next): engine differential testing against the real Go Ikemen (wave B 6–7), ZIP characters,
the dark rectangle seen once behind KFM in the developer command-test screen (wave A 4), gamepad
navigation inside the settings pages, Simul / tag run-in / team HUD, keystore + AAB.
Seen in the dev.8 probe run (kfm720 vs kfm720, AI vs AI): P2 kept P1 in a get-hit loop with
state 440 for ~15 s (life 1000 → 23) — possibly missing juggle-point limits; to be checked against
Go Ikemen in wave B.

## Evidence (release `v0.1.0-dev.8`)

| Gate | Result |
| --- | --- |
| Compile (Unity 2022.3.62f3) | 0 errors |
| EditMode tests | **275 / 275** passed (new: `Compiler_EvaluatesIfConditionOnce_RandomDoesNotSplitBranches`) |
| UI regression `[IK UI]` | checks=177 errors=0 at 2400×1080, 1024×768, 1280×720 |
| Harness `IK_CTRL0`, 20 matches | stuck-without-control events: dev.7 **13** → dev.8 **0** |
| FTL robo + probe `probe8c` (MediumPhone.arm-34, `ar`) | Passed. Title background at 1 s (bg elements = 9, no decode error); motif ready **1189 ms** (dev.7: 6096 ms); match load 776 ms (720p) / 72 ms (240p) |
| FTL robo + probe `probe8d` (after the RTL settings / About fix) | Passed. Motif ready 1270 ms; settings labels right-aligned, no overlap on any page; About lines no longer overlap |
| APK `my-ikmen-dev8.apk` | 40,825,477 bytes, sha256 `673e12dc4b4b4c13ed60b24634f58723ee981cdf71a13387c0f4d49f222a7886`, `com.ayoub.ikmen` versionCode 8 / versionName 0.1.0-dev.8, minSdk 23, targetSdk 36, arm64 IL2CPP, debug-signed (apksigner verify OK) |

Emulator FPS is SwiftShader (software) and is not reported; phone numbers must come from the Poco F3.

## 0. How the findings were measured

| Source | What | Result |
| --- | --- | --- |
| Firebase Test Lab, robo, dev.7 APK | MediumPhone.arm, Android 14, 2400×1080, `ar` locale (virtual device, SwiftShader software GPU) | No crash. Screenshots + video. |
| Firebase Test Lab, robo, `0.1.0-dev.8-probe` APK | same device; `DeviceProbe` runs automatically, pulls `files/probe/` | `probe.log` + 13 screenshots, see §1 |
| Linux player, `-ikprobe`, 2400×1080 | same probe on the sandbox | motif 4.1 s, max frame 131 ms, bench avg 78.5 FPS / 1 % low 69.6 |

Virtual devices only (AGENTS.md §4). The emulator GPU is software: its FPS (≈30–40) is **not** a
phone number. Frame pacing / FPS must come from the owner's Poco F3 (Options → Video → Benchmark,
or the probe build).

## 1. Findings (evidence)

| # | Owner symptom | What the device shows | Cause |
| --- | --- | --- | --- |
| F1 | No background | Title screen is **black for ~6 s** after launch (`motif ready after 6096 ms`, screenshot at 1 s is black, at 8 s the sky + logo are there). On 20:9 the screenpack art (16:9) leaves **black bars** left and right. The dev.7 robo run's title screenshots were all black too. | `MotifAssets` decodes the whole 9 MB `system.sff` (every PNG sprite, eagerly) before anything is drawn; no loading state. Art is not scaled / extended to the screen width. On a slower phone it may take much longer or fail (`LoadError` is silent) — to be confirmed on the Poco F3 with the probe build. |
| F2 | Settings navigation | Rows run **off the bottom** of the screen on Controls, Game and Video (last row cut, no scrolling). The panel is semi-transparent: the screen behind ("اختبار الأوامر tick 0") shows through. About page: the three text lines **overlap**. | `SettingsMenu` has no `ScrollRect`, fixed `RowHeight` 96, panel alpha 0.96, About lines placed with fixed offsets. |
| F3 | Title text | Arabic menu lines **overlap** each other (line height smaller than the Amiri glyph height); footer still says **"dev.6"**. | `TitleScreen` spacing; hard-coded footer string. |
| F4 | Sprites | dev.7 robo run: a dark blue rectangle behind KFM on some frames of the command-test screen. Not seen in the probe's match screenshots. | Investigate: sprite bleed / transparent texels / GLES shader path. |
| F5 | Jitter | Not reproducible on the emulator (software GPU, frames 20–40 ms all the time). The two ~2.5 s freezes in `probe.log` (t=329→339, 668→679) line up exactly with the test robot pressing HOME (app paused) — **not** a game bug. | Needs phone numbers: probe / benchmark on the Poco F3. |
| F6 | Player gets stuck | Seen on Linux: a fighter in state 0 with `ctrl=0` for many ticks in the middle of a round. Not proven to be the owner's "stuck" yet. | Engine accuracy (see F7) or input; needs a per-tick detector and the owner's screen recording. |
| F7 | "thousands of errors" | The engine is a C# re-implementation; dev.7 parity only compares this engine with itself (27 traces). There is **no comparison with the real Ikemen GO** yet. | Differential testing against Go Ikemen (§2 wave B). |
| F8 | "small" | The APK has only Ikemen GO's default content: KFM ×4 (kfm, kfm720, kfm_zss, kfm_zaxis) and 6 stages. The full game the owner plays on PC has many more characters / stages. | Content import (§2 wave C). |

## 2. Plan

### Wave A — device bugs (first, small, visible)
1. **Title background:** decode the title background sprites first (lazy SFF: index all, decode on
   demand), show a loading state, never a black screen; log `LoadError` on screen; scale / extend
   the art to fill 20:9 (no black bars). Same for select / versus / fight backgrounds.
2. **Settings:** scrollable pages (`ScrollRect` + drag + focus follows selection), opaque panel,
   RTL row layout, About page re-flow, gamepad / back-button navigation.
3. **Title menu:** Arabic line spacing from font metrics; footer = `Application.version`.
4. **Dark rectangle behind sprites:** reproduce on GLES (probe screenshot of the command-test
   screen) and fix.
5. **Probe in every release:** `DeviceProbe` + FTL robo run become a gate (`TESTING.md`), with
   screenshots of title / settings / select / match attached to the release notes.

### Wave B — engine accuracy ("the code was not translated well")
6. Build the real Go Ikemen engine (`engine/ikemen-go`) headless in the sandbox and record golden
   per-tick traces (state, anim, pos, vel, life, power, ctrl, hit flags) for scripted inputs.
7. Run the same inputs through the C# engine; every first-divergence tick becomes a bug ticket,
   fixed one by one (HitBy / ProjHit approximations, guard, juggle, binds, helpers, projectiles…).
8. Per-tick "stuck" detector (state 0 / ctrl 0 too long, state longer than its anim, no exit) in
   the harness and in the probe.

### Wave C — content ("the original full game has more")
9. Import MUGEN / Ikemen characters, stages and screenpacks from phone storage
   (`/sdcard/Ikemen/chars`, `stages`, `data`), with `select.def` editing in the app.
10. Ship the owner's PC content in the APK / an asset pack once the folders are sent and the
    licences allow redistribution.

### Wave D — carried over from dev.6 / dev.7
Simul, tag run-in + team HUD, palette-index textures, Z / 3D stages, owner keystore + AAB.

## 3. Needed from the owner
- The PC game's `chars`, `stages`, `data` folders (or a list of what is in them).
- A short screen recording of the jitter and of a "stuck" player on the Poco F3.
- Run the probe build (`0.1.0-dev.8-probe`, starts the test by itself) once and send
  `Android/data/com.ayoub.ikmen/files/probe/probe.log`.
