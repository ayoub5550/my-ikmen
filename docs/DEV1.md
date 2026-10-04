# dev.1 — Unity skeleton + input, settings and layout layer

Date: 2026-10-04 · Branch `dev.1` (stacked on `docs/unity-roadmap`) · Unity 2022.3.62f3 LTS

This milestone creates the Unity project and the complete control layer specified in
`docs/TOUCH_AND_SETTINGS.md`. **There is no fight engine yet** (dev.2–dev.4). The gate for
judging this milestone is the *Input Test* screen: on-screen controls → `InputFrame` at a
fixed 60 ticks/s → MUGEN command recognition, shown live.

---

## 1. What was built

| Area | Files (`unity/Assets/IK/`) | Notes |
| --- | --- | --- |
| Input model | `Scripts/Input/InputFrame.cs` | 14 logical inputs, bit order identical to `InputBits` in `engine/ikemen-go/src/input.go` |
| Input maths | `Scripts/Input/InputLogic.cs` | 8-way quantisation (cardinal 50°, diagonal 40°), **SOCD modes 0–4 ported 1:1** from `InputReader.SocdResolution`, **button assist** ported from `ButtonAssistCheck` |
| Touch | `Scripts/Input/TouchControls.cs`, `HoldButton.cs`, `DirectionPad.cs` | D-pad / floating stick / fixed stick, 6 attack buttons + START/PAUSE (+ optional D/W and macros), true holds, slide-to-press, multi-touch ownership, safe-area anchoring |
| Routing | `Scripts/Input/InputRouter.cs` | 60 Hz logic tick independent of render FPS, latching so a sub-tick tap is never lost, device auto-hide, SOCD + assist applied once per tick |
| Gamepad / keyboard | `Scripts/Input/GamepadInput.cs` | Input System when the package is present (`IK_USE_INPUT_SYSTEM` via asmdef versionDefines), legacy fallback otherwise; defaults mirror `[Keys_P1]`/`[Joystick_P1]` |
| Commands | `Scripts/Input/CommandRecognizer.cs` | Minimal MUGEN recogniser (`command.time = 15`), longest match wins; replaced by the real CMD buffer in dev.3, the test cases stay |
| Settings | `Scripts/Settings/GameSettings.cs`, `SettingsStore.cs` | every setting of §5, JSON in `persistentDataPath`, `schemaVersion` + migration, debounced write + flush on pause/quit |
| Layouts | `Scripts/Settings/ControlLayout.cs` | positions in reference units relative to an anchor corner, presets Default / Left-handed / Compact, 3 slots, validation (min target, safe area, overlap, HUD zone) |
| UI | `Scripts/UI/*` | code-built uGUI (no prefabs), 1280×720 reference, match = height; main menu, settings with 6 pages, layout editor, input display |
| Arabic | `Scripts/UI/ArabicShaper.cs`, `Loc.cs` | hand-written joiner (presentation forms U+FE70–FEFC, lam-alef ligatures) + run-level RTL reordering, because legacy uGUI `Text` does no shaping |
| Build | `Editor/IKBuildPipeline.cs` | scene creation, player settings, APK build — all from batch mode |
| Fixtures | `Scripts/App/IKUIRegressionRunner.cs`, `Editor/IKUIRegression.cs` | rendered UI + synthetic pointer tests |

## 2. Evidence (all re-runnable, see TESTING.md)

| Gate | Command | Result |
| --- | --- | --- |
| 1 compile | `-batchmode -nographics -quit` | **0 `error CS`**, `Exiting batchmode successfully` |
| 2 EditMode | `-runTests -testPlatform EditMode` | **38 / 38 passed** (`Builds/validation/editmode.xml`) |
| 3 rendered UI | `IK_UI_FIXTURE=1` + Xvfb + `-force-glcore` | **56 checks, 0 errors** (`Builds/validation/dev1/ui-regression-*.json` + PNGs) |
| 4 APK | `IKBuildPipeline.BuildAndroid` | `com.ayoub.ikmen` 0.1.0 (code 1), minSdk 23, targetSdk 36, **arm64-v8a only**, IL2CPP |
| 4 device (virtual) | Firebase Test Lab, `MediumPhone.arm` API 34, Robo | see §5 |
| 5 device (physical) | Poco F3 | **owner gate — not done, nothing here claims it** |

EditMode cases map to `docs/TOUCH_AND_SETTINGS.md` §6.1–§6.7; rendered cases to §6.8–§6.11.

## 3. Decisions taken in this milestone

1. **Touch via uGUI pointer events** (`HoldButton`, `DirectionPad`), **gamepad/keyboard via the
   Input System package** with a legacy fallback compiled when the package is absent.
   `activeInputHandler = 2` (both), so `Input.touchCount` and the Android back key still work.
2. **Arabic is shaped in our own code.** Modern Noto Arabic fonts do not ship the Unicode
   presentation-forms block, so the UI font is **Amiri (OFL)** and `ArabicShaper` maps every
   letter to its contextual form. Licence copied to `StreamingAssets/Licenses/OFL-Amiri.txt`.
3. **Minimum touch target 100 reference units wins over the spec's 90-unit PAUSE button**:
   `ControlLayout.RectOf` clamps size *and* keeps every control inside the safe area, so a
   larger "button size" setting can never push a control off-screen.
4. **The layout editor refuses to save an invalid layout** (overlap / outside safe area) and
   shows why; the previously saved layout is kept. Verified in the rendered fixture.
5. **No screenpack content is shipped yet.** dev.1 is engine-less, so `StreamingAssets`
   carries only licences. The 108 MB of MUGEN content arrives with the dev.2 loaders.
6. Package set trimmed to what the game uses: Unity Ads / Analytics / Purchasing were
   removed, so the APK no longer requests `AD_ID` or `BILLING`.

## 4. Known limits (do not read these as "done")

- The Editor game view is **640×480** in batch mode, so the rendered PNGs are 4:3. Layout
  correctness at 16:9, 20:9 (with a punch-hole inset), 4:3 and 21:9 is therefore checked
  **geometrically** (computed safe areas) in both EditMode and the rendered fixture — that is
  not the same as looking at a 2400×1080 screenshot.
- Synthetic `PointerEventData` presses are not real multi-touch. Two-thumb behaviour,
  haptics and feel can only be judged on the Poco F3.
- The command recogniser is deliberately small: `~X` is evaluated as "X not held", not as a
  release edge with timing, and there is no charge (`$`, `/`) timing yet. dev.3 replaces it.
- APK is **debug-signed**; release signing/keystore belongs to dev.7.
- Haptics call `Handheld.Vibrate()` (Android only) and are not exercised by any automated gate.

## 5. Firebase Test Lab (virtual device only)

```sh
gcloud firebase test android run --type robo \
  --app unity/Builds/my-ikmen.apk \
  --device model=MediumPhone.arm,version=34,locale=ar,orientation=landscape \
  --timeout 5m
```

Result is recorded in `Builds/validation/dev1/ftl-*.txt` (and summarised in the PR). Only
virtual models are ever used, per `AGENTS.md` §4.

## 6. Next (dev.2)

SFF v1/v2, AIR, DEF and SND loaders → Kung Fu Man animating in a viewer, reusing this input
layer for the debug controls.
