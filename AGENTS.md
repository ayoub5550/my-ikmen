# AGENTS.md — guide for developers and AI agents continuing this project

Read this file fully before touching anything. It is written for coding agents and humans.
Keep it up to date whenever you change the pipeline, the layout or a decision.
The owner communicates in **Arabic**; code, comments and this file are in **English**;
`README.md` is Arabic + English.

This repository follows the same method that worked for the owner's earlier Unity port
**my-librequake** (`ayoub5550/my-librequake`, private, reference tag `v0.2.0` =
`edb206c6a0c0141eaf81fb527a6a4af46a2ce896`). Where this guide says "as in LibreQuake",
open the named file at that tag.

---

## 1. What this project is

An Android **2D fighting game** built on **Unity**, reading M.U.G.E.N / Ikemen GO content
(characters `.def/.sff/.air/.cmd/.cns/.snd`, stages, screenpack). Nothing of the Go engine
runs in the game: the engine is **re-implemented in C#** inside Unity, using
`engine/ikemen-go/src` (Go, MIT) as the behavioural reference. Decision record: ADR-001 in
`docs/ROADMAP.md` (option A). Do not reopen it without the owner.

Owner priority for the next agent (2026-10-04): **touch controls, on-screen buttons and the
settings menu must reach (and exceed) the quality of my-librequake v0.2.0**. The full
specification with acceptance tests is **`docs/TOUCH_AND_SETTINGS.md`** — it is the
contract for that work.

Status at hand-over (2026-10-04):

- Upstream resources imported, verified (2,596 files): engine source v1.0.0 and the
  Screenpack (see `THIRD_PARTY_NOTICES.md` for commits and licences).
- No Unity project yet (`unity/` does not exist). No Unity toolchain is installed in the
  sandbox right now — §6 explains how it was installed for LibreQuake.
- Important fact: Ikemen GO v1.0.0 already ships an **official Android build**
  (`build/build_android.sh`, release asset `Ikemen_GO-v1.0.0-android.zip`, Android 13+,
  arm64). It has **no on-screen touch controls** (gamepad/keyboard only; touch is only SDL's
  mouse emulation, see `src/main.go` `SDL_ANDROID_SEPARATE_MOUSE_AND_TOUCH`). It is a useful
  reference APK for behaviour, not our product.

## 2. Repository map

| Path | What it is |
| --- | --- |
| `engine/ikemen-go/` | Ikemen GO v1.0.0 source (Go, MIT). **Reference only, never built into the game.** Input: `src/input.go`, `src/input_sdl.go`; command parsing: `src/char.go`, `src/bytecode.go`; defaults: `src/resources/defaultConfig.ini`. |
| `assets/screenpack/` | Default content: `chars/kfm*` (Kung Fu Man variants), `stages/`, `data/` (system.def, fight.def, lifebars), `font/`, `sound/`, `video/`. |
| `docs/ROADMAP.md` | Milestones dev.1 → dev.7 and ADR-001 (Arabic). |
| `docs/TOUCH_AND_SETTINGS.md` | **Spec for touch controls, buttons and settings** (English). |
| `docs/DEV<n>.md` | One doc per milestone: what changed, evidence, open gates. Create it with the milestone. |
| `TESTING.md` | Test layers and commands. |
| `tools/sandbox/` | gVisor sandbox fixes copied from LibreQuake (Unity shader compiler under qemu, FMOD shim, launcher). |
| `unity/` | **To be created in dev.1.** Unity project root (`unity/Assets/IK/...`, namespace `IK`). |

Planned Unity layout (create in dev.1, keep it):

```
unity/Assets/IK/Scripts/Core      SFF/AIR/DEF/SND loaders, palettes (dev.2)
unity/Assets/IK/Scripts/Fight     CMD buffer, CNS state machine, triggers, collisions (dev.3-4)
unity/Assets/IK/Scripts/Input     InputFrame, TouchControls, HoldButton, DPad, GamepadInput, InputRouter
unity/Assets/IK/Scripts/Settings  GameSettings (data), SettingsStore (JSON), defaults mirroring defaultConfig.ini
unity/Assets/IK/Scripts/UI        UIKit, skin, MainMenu, SettingsMenu, LayoutEditor, InputDisplay
unity/Assets/IK/Editor            IKBuildPipeline (BuildAndroid, BuildAndroidStore), UI regression runner
unity/Assets/IK/Resources         anything loaded at runtime by name (shaders, fonts, UI sprites)
unity/Assets/StreamingAssets/ikemen   copied content (chars/stages/data/font/sound) read at runtime
```

## 3. Development workflow — follow this order (same as LibreQuake)

1. **Baseline.** Read this guide, `TESTING.md`, `docs/TOUCH_AND_SETTINGS.md`, the open
   PRs/branches. Work in a task branch/worktree, never directly on `main`.
2. **Reproduce and classify** every bug: screen size, what was pressed, expected vs actual,
   logs. Distinguish port defect / missing asset / upstream behaviour / tooling failure.
3. **Cheap checks first:** pure C# unit tests (EditMode), then rendered UI fixtures, then APK.
4. **Fix the smallest proven cause.** Compare with the Go reference for engine semantics.
   Never add cheats to make a test pass.
5. **Compile, then test one behaviour at a time.** Rendered tests and device tests are
   separate gates; a passing synthetic pointer test is not phone QA — say so in docs.
6. **Build only when useful.** Inspect the actual APK (package, version, ABIs, signature).
7. **Hand off:** push a branch + PR, write `docs/DEV<n>.md` with evidence and limits,
   update this file. No "bug-free" claims without evidence.

## 4. Owner rules (non-negotiable)

- **Merge only on the owner's explicit word** («ادمج» / «ادمجه»). Open a PR and wait.
- After each milestone: upload the APK to the owner and publish a **GitHub Release (not a
  pre-release)** with the APK asset, tag `v0.1.0-dev.<n>`.
- Automated device testing only on **Firebase Test Lab virtual devices**
  (`MediumPhone.arm`, API 34). Never physical FTL devices, never anything billed. The
  owner's Poco F3 (Adreno 650, 20:9 with punch-hole) is the physical gate.
- Use the whole machine (17 cores): `-j17`, `LP_NUM_THREADS=17`. Note `nproc` can print 1
  because of OpenMP env limits — check `env -u OMP_NUM_THREADS -u OMP_THREAD_LIMIT nproc`.
- **Secrets** (Unity password, keystore, Firebase service account) live only outside the
  repo (e.g. `/work/native/secrets/`, chmod 600). Never in git, docs, logs, APKs or PR text.
- No Unity Cloud Build, no paid services without the owner's approval.

## 5. Licences (matters: the owner wants to sell this later)

- Our code: MIT (`LICENSE`). Ikemen GO: MIT — keep its notice.
- Screenpack art: CC BY 3.0 — commercial OK **with credits** shown in-game (About/Credits).
- Elecbyte fonts: CC BY-NC 3.0 and **Kung Fu Man** (Elecbyte): no clear commercial licence →
  development placeholders only. Must be replaced before dev.7. Keep that list in
  `THIRD_PARTY_NOTICES.md` current. Never add copyrighted MUGEN characters from the web.
- UI fonts for Arabic/Latin labels: use OFL fonts (e.g. Noto Sans Arabic / Cairo) and ship
  their licence under `StreamingAssets/Licenses/`.

## 6. Toolchain (proven on LibreQuake, reuse it)

- **Unity 2022.3.62f3 LTS** (`96770f904ca7`) — the exact version whose Linux + Android
  module install, IL2CPP builds and sandbox fixes are proven. Do not switch to Unity 6
  without a reason and the owner's OK. Android: IL2CPP, **ARM64** (+ARMv7 optional),
  minSdk 23, targetSdk 36, package `com.ayoub.ikmen` (confirm name with owner before store).
- The Editor needs a Unity licence: the owner's Unity account (Personal). Ask the owner for
  credentials; store outside the repo; reuse an existing activation if present.
- **Sandbox fixes (gVisor, no GPU, no root)** — files in `tools/sandbox/`:
  - `UnityShaderCompiler.wrapper.sh`: the native shader compiler crashes in `PESetupFS`
    (arch_prctl FS/GS). Rename the binary to `UnityShaderCompiler.real` once and put the
    wrapper in its place; it runs only that binary under `qemu-x86_64-static` (download the
    Debian `qemu-user-static` package with `apt-get download` + `dpkg -x`, no root).
  - `schedfix.c` → `gcc -shared -fPIC -O2 -o /work/unity/shim/libschedfix.so schedfix.c`:
    gVisor rejects realtime thread priorities, FMOD then aborts ("Unable to initialize any
    audio device"). Preloaded by `run_unity.sh`. Keep audio enabled.
  - `run_unity.sh`: launcher (library path for extracted GTK libs, HOME, LD_PRELOAD).
- Install lessons: get URLs/checksums from
  `https://services.api.unity.com/unity/editor/release/v1/releases?version=2022.3.62f3`.
  The Linux editor archive does **not** contain AndroidPlayer; the Android module is the
  `UnitySetup-Android-Support-for-Editor-2022.3.62f3.pkg` (XAR → gzip cpio Payload) —
  extract without running scripts into `Editor/Data/PlaybackEngines/AndroidPlayer`. Install
  Android command-line tools **6.0** specifically (only `latest` → Unity reports tools
  missing), build-tools 34.0.0, platform 36, NDK r23b, JDK 11 for Gradle. Missing GTK libs:
  `apt-get download libgtk-3-0` etc. and extract into `/work/unity/libs`.
- Rendering without GPU: `xvfb-run -a -s "-screen 0 1280x720x24"`, `-force-glcore`
  (never `-nographics` for rendered tests), `LP_NUM_THREADS=17` (llvmpipe hangs otherwise).
- One Unity instance per project. Stale `Unity.ILPP.Runner` → "Invalid ILPostProcessor
  configuration" with zero CS errors: kill only your own stale processes, delete
  `/tmp/ilpp.sock-*` and `Temp/UnityLockfile`.

## 7. Unity rules that bit LibreQuake (do not repeat)

- Anything loaded with `Resources.Load` / `Shader.Find` must live under a `Resources/`
  folder or be referenced by a serialized asset — otherwise it is **stripped** from the
  player (LibreQuake shipped invisible monsters because of this). Pin custom shaders in
  `GraphicsSettings.m_AlwaysIncludedShaders` too.
- Every MonoBehaviour serialized into a scene/prefab must be in a file with the **same name
  as the class** (else "Script attached to … is missing" only in the build log).
- `GetComponent<T>() ?? AddComponent<T>()` is broken (Unity fake-null) — use a `GetOrAdd<T>()`
  extension.
- Never clear static registries in `Awake` (undefined order) — prune destroyed entries.
- Custom UI `Graphic`s can pass pointer tests but render blank — always add a **rendered
  pixel check** for labels/buttons (LibreQuake v0.2.0 lesson).
- `Application.targetFrameRate = 60` on Android; fighting logic runs at a **fixed 60 ticks/s**
  independent of render FPS (MUGEN timing). Read input once per logic tick.

## 8. What to do next (priority order)

1. **dev.1 — skeleton + input layer** (see ROADMAP): Unity project, build pipeline, APK on
   the Firebase virtual device, and the complete touch/gamepad/settings layer from
   `docs/TOUCH_AND_SETTINGS.md` tested with an **Input Test scene** (training-mode style
   input display that shows MUGEN commands like `~D, DF, F, x` being recognised). This lets
   the controls be perfected before the fight engine exists.
2. dev.2 loaders → dev.3 CMD/CNS → dev.4 fight → dev.5 AI/gamepad polish → dev.6 menus/Arabic
   → dev.7 release (replace placeholder content).
3. Keep `docs/DEV<n>.md`, this file and the README in sync at every milestone.
