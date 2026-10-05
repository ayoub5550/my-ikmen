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

Do not pass `-quit` with `-runTests`. Required cases: `docs/TOUCH_AND_SETTINGS.md` §6 (1–7).

## 3. Rendered UI fixture

```sh
IK_UI_FIXTURE=1 LP_NUM_THREADS=17 timeout -k 10 300 xvfb-run -a -s "-screen 0 1280x720x24" \
  "$UNITY" -batchmode -force-glcore -projectPath "$PROJECT" \
  -executeMethod IK.EditorTools.IKUIRegression.Run -logFile "$PROJECT/Builds/validation/ui.log"
```

No `-quit` (the runner exits itself with code 0 = pass). Use `-force-glcore`, never
`-nographics`. Repeat at 2400×1080 (`-screen 0 2400x1080x24`) and 1024×768.
Required cases: §6 (8–11). Output: JSON report + PNGs in `Builds/validation/`.

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
restart, Arabic menus. Ask the owner for a screen recording when a report is vague.
