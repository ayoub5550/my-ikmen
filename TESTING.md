# TESTING.md — how to test without a phone

Same layered method as my-librequake. Run cheapest first; each layer is a separate gate.
Never call a synthetic or rendered test "device QA".

| Layer | Needs | Catches |
| --- | --- | --- |
| 0. Static checks | Python | content files present, licence list, no secrets staged |
| 1. Compile check | Unity Editor, headless | C# errors, missing scripts |
| 2. EditMode tests | Unity Test Framework, headless | input logic, SOCD, button assist, settings, layouts, loaders |
| 3. Rendered UI fixture | Unity + Xvfb + llvmpipe + qemu shader wrapper | blank labels, pressed state, menus, safe area |
| 4. Firebase virtual device | APK + FTL `MediumPhone.arm` API 34 | install, launch, crash, screenshots |
| 5. Owner's phone | Poco F3 | real multi-touch, feel, performance |

Commands assume `UNITY=/work/unity/run_unity.sh` (see `tools/sandbox/` and AGENTS.md §6) and
`PROJECT=$PWD/unity` from the task worktree. Never put credentials on the command line in
docs or logs; reuse the local activation.

## 1. Compile check

```sh
mkdir -p unity/Builds/validation
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -logFile "$PROJECT/Builds/validation/compile.log"
grep "error CS" "$PROJECT/Builds/validation/compile.log"        # must print nothing
grep "Exiting batchmode successfully" "$PROJECT/Builds/validation/compile.log"
```

## 2. EditMode tests

```sh
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" -runTests -testPlatform EditMode \
  -testResults "$PROJECT/Builds/validation/editmode.xml" -logFile "$PROJECT/Builds/validation/editmode.log"
```

Do not pass `-quit` with `-runTests`. Required cases: `docs/TOUCH_AND_SETTINGS.md` §6 (1–7),
since dev.2 the loader cases in `Assets/IK/Tests/EditMode/MugenLoaderTests.cs`, and since
dev.3 the engine cases in `Assets/IK/Tests/EditMode/FightEngineTests.cs` (expressions,
commands, states, the KFM state machine and a 20 000-tick random-input soak). Since dev.4 also the hit system (`HitSystemTests.cs`), the stage
(`StageTests.cs`, `StageRenderTests.cs`), the motif (`FightDefTests.cs`) and the HUD
(`FightHudTests.cs`). Since dev.5 also `Dev5EngineTests.cs` (helpers, explods, projectiles,
redirection, superpause, CPU), `ZssTests.cs`, `MotifTests.cs` and `RosterTests.cs`. Current total: **274 cases** (dev.6 added `Dev6EngineTests.cs` and `Dev6TeamTests.cs`, dev.7 `Dev7PerfTests.cs`).

Engine expectations are taken from the character's own files or from the Go reference
(`engine/ikemen-go/src/*.go`, `engine/ikemen-go/data/common1.cns.zss`). If one fails, read
the source data first; never relax the expectation.

### Regenerating the loader fixtures (only when a format changes)

```sh
python3 tools/sff_dump.py assets/screenpack/chars/kfm/kfm.sff \
  -o unity/Assets/IK/Tests/EditMode/Fixtures/kfm_sff.json
python3 tools/sff_dump.py assets/screenpack/chars/kfm/intro.sff \
  -o unity/Assets/IK/Tests/EditMode/Fixtures/kfm_intro_sff.json
python3 tools/sff_dump.py assets/screenpack/font/arcade.sff --stage \
  -o unity/Assets/IK/Tests/EditMode/Fixtures/arcade_sff.json
python3 tools/sff_dump.py assets/screenpack/data/fightfx.sff --stage \
  -o unity/Assets/IK/Tests/EditMode/Fixtures/fightfx_sff.json
```

`tools/sff_dump.py` is the independent Python reference decoder; `--png DIR` also writes the
decoded sprites as PNGs for eyeballing. Never regenerate a fixture to make a failing test
pass — first prove the C# decoder is right.

### Headless engine harness (no Unity, seconds)

```sh
tools/harness/build.sh            # UNITY_EDITOR_DIR=<Unity>/Editor if not /work/unity/editor/Editor
"$UNITY_EDITOR_DIR/Data/MonoBleedingEdge/bin/mono" tools/harness/h.exe kfm_zss kfm 600 7200
```

Runs a CPU-vs-CPU match from the repository root and lists unknown triggers, controllers and ZSS
warnings. It is a development aid, not a gate.

## 3. Rendered UI fixture

```sh
IK_UI_FIXTURE=1 LP_NUM_THREADS=17 timeout -k 10 300 xvfb-run -a -s "-screen 0 1280x720x24" \
  "$UNITY" -batchmode -force-glcore -projectPath "$PROJECT" \
  -executeMethod IK.EditorTools.IKUIRegression.Run -logFile "$PROJECT/Builds/validation/ui.log"
```

No `-quit` (the runner exits itself with code 0 = pass). Use `-force-glcore`, never
`-nographics`. Repeat at 2400×1080 (`-screen 0 2400x1080x24`) and 1024×768.
Required cases: §6 (8–11) plus, since dev.3, §13 — the training screen: the fighter is
drawn, walking moves it exactly `walk.fwd` per tick, the punch enters state 200 and fires
one HitDef, the jump lands on y = 0, a quarter circle through *input frames* reaches state
1000, Clsn boxes are drawn and the HUD text really renders. Since dev.4 also §14 — the fight screen: the stage art is on
screen, the camera scrolls the background, the screenpack HUD is built from `fight.sff`, a
punch takes exactly the KFM damage, Clsn boxes are drawn and a scripted round reaches a KO.
Since dev.5 also the front end: title (English and Arabic), select, stage select, VS, victory,
continue, survival results, credits and a menu → fight flow. Current total: **177 checks** (dev.6 added the team flow: team menu, member picks, 2-vs-2 tag fight, TAG button, music; dev.7 the controls drawn on top of the training screen, by pixels). The batch game view is 640×480 whatever `-screen` says, so a
1280×720 motif is clipped horizontally in the PNGs — judge the HUD geometry from the numbers,
not from the fixture crop.
Output: JSON report + PNGs in `Builds/validation/` (`IK_UI_DIR` picks the folder).

## 3b. Performance (dev.7)

- **Harness:** `IK_PERF=1 mono tools/harness/h.exe kfm720 kfm_zss` → bytes allocated and ms per
  tick, engine vs AI (dev.7: ~1.5 KB / 0.2 ms per tick).
- **Editor benchmark:** `-executeMethod IK.EditorTools.IKBench.Run` with `IK_BENCH_OUT=file.json`
  (`IK_BENCH_FRAMES`, `IK_BENCH_FILTER` 0/1/2). Editor numbers include editor overhead.
- **Linux test player** (closest to a device in the sandbox): `-executeMethod
  IK.EditorTools.IKBuildPipeline.BuildLinux -buildTarget Linux64` → `Builds/linux/ikmen.x86_64`, then
  `xvfb-run -s "-screen 0 2400x1080x24" ./ikmen.x86_64 -screen-width 2400 -screen-height 1080
  -screen-fullscreen 0 -force-glcore -ikbench out.json [-ikframes 3600] [-ikfilter 2]`.
  `-ikshots dir` instead saves 2400×1080 screenshots of both button styles (idle / pressed) over a
  real match and of the three pixel filters. llvmpipe is a software GPU: judge CPU columns.
- **On the phone:** Options → Video → Benchmark (also writes `benchmark.json` in the app's
  persistent data folder). Show FPS turns on the live overlay.

## 4. Firebase Test Lab (virtual only)

```sh
gcloud firebase test android run --type robo --app unity/Builds/my-ikmen.apk \
  --device model=MediumPhone.arm,version=34,locale=ar,orientation=landscape --timeout 5m
```

Credentials: the owner's Firebase service account, stored outside the repo. Never use
physical device models, never anything billed.

## 5. Owner device checklist (Poco F3)

Install over the previous build, then: QCF+LP, DP+HP, `x+y`, hold-back charge, two-thumb
simultaneous press, pause/resume from home button, punch-hole side, settings persist after
restart, Arabic menus; dev.7: the buttons are visible in a match, II pauses, Options → Video →
Benchmark result screenshot, render scale 70 % and Crisp look. Ask the owner for a screen recording when a report is vague.
