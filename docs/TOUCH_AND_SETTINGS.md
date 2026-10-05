# Touch controls, on-screen buttons and settings — specification

Status: **specification, nothing implemented yet** (2026-10-04).
Audience: the agent implementing dev.1 input layer (and later polishing in dev.5/dev.6).
Quality bar: my-librequake v0.2.0 controls (`Assets/LQ/Scripts/UI/TouchControls.cs`,
`HoldButton.cs`, `ClassicUI.cs`, `ClassicLabel.cs`, `UIKit.cs`, `MainMenu.cs`,
`Game/LQUIRegressionRunner.cs`, `RELEASE-0.2.0.md` at tag `v0.2.0`). Everything that
worked there is carried over (§2); a fighting game needs more (§3–§5).

---

## 1. The input model we must feed (from Ikemen GO)

Ikemen reads **14 logical inputs per player per tick** (`engine/ikemen-go/src/input.go`,
`InputBits`): `U D L R a b c x y z s d w m`.

| Logical | Meaning | Keyboard P1 default | Gamepad default |
| --- | --- | --- | --- |
| U D L R | directions (engine converts L/R to B/F by facing) | arrows | D-pad / left stick |
| a b c | kicks (light / medium / heavy) | z x c | A B RT |
| x y z | punches (light / medium / heavy) | a s d | X Y RB |
| s | start / taunt | Enter | START |
| d, w | Ikemen extra buttons (tag/assist in some chars) | q w | LB LT |
| m | menu / pause | — | BACK |

Rules the touch layer must respect:

- Output **digital** U/D/L/R — never L and R (or U and D) together. Apply **SOCD
  resolution** like `InputReader.SocdResolution` (config `SOCDResolution`, Ikemen default
  `4` = deny both). Implement modes 0–4 identically and unit-test them.
- **Button assist** (`ButtonAssist = 1`): a press is delayed one tick so `x+y` style
  simultaneous presses are easier (`ButtonAssistCheck`). Port it as a pure function.
- Read input once per **logic tick (60 Hz)**, not per render frame. Touch events arriving
  between ticks must not be lost: latch "pressed since last tick" so a 1-frame tap still
  registers.
- Produce an immutable `InputFrame` struct (14 bools + analog stick) consumed by the engine,
  the menus, replays and the AI later. Touch, keyboard and gamepad all produce the same
  struct; `InputRouter` ORs them (last active device wins for the on-screen hint glyphs).

Example commands that must be performable on touch (from `assets/screenpack/chars/kfm/kfm.cmd`,
`command.time = 15`): `~D, DF, F, x` (QCF), `~D, DB, B, y` (QCB), `~F, D, DF, x` (DP),
`~D, DF, F, D, DF, F, x` (super), `x+y`, `$F, x`.

## 2. Carried over from LibreQuake v0.2.0 (proven, keep)

1. **Code-built UI** (`UIKit`): one Canvas, `CanvasScaler` *Scale With Screen Size*,
   reference **1280×720**, `matchWidthOrHeight = 1` (height). No hand-made prefabs needed.
2. **`HoldButton`** (`IPointerDownHandler/IPointerUpHandler`): remembers the `pointerId`
   that pressed it; **only that finger releases it**; a second finger cannot steal an
   active press; `OnDisable` releases; slide-off keeps holding.
3. **Release everything** on `SetVisible(false)`, pause, `OnApplicationFocus(false)` /
   `OnApplicationPause(true)`. After resume a **fresh press is required** (no stuck inputs).
4. **Safe area:** the touch root is anchored to `Screen.safeArea` and re-applied whenever
   safe area or resolution changes (notches, punch-holes, rotation, split-screen).
5. **Positions are button centres relative to a corner** (centre pivot), in reference units.
   Minimum hit target **100 units**; big primary targets ~150+.
6. **Visible pressed state:** tint + outline + label colour change, verified by pixel diff.
7. **Dynamic floating stick** on the left side: appears where the thumb lands, knob
   follows, hides on release.
8. **Classic skin** built from the game's own artwork (LibreQuake used `conchars` + stone
   frame). Here: build the skin from screenpack art (lifebar/frame sprites in
   `assets/screenpack/data/`) or simple vector frames; labels must render (pixel check).
9. **Settings persisted immediately** on change; options panel opened from main menu and
   from the pause menu; Back returns to the previous panel.
10. **UI regression runner** in the real scene (`LQUIRegressionRunner` pattern): env var
    enables it, it drives buttons via synthetic `PointerEventData`, checks layouts at
    several aspect ratios, captures PNGs and checks pixels, writes a JSON report, exits.

## 3. Fighting-game touch layout (new)

Landscape only. Reference 1280×720, all values in reference units, centres.

### 3.1 Left: direction control (three selectable modes)

| Mode | Behaviour | Default |
| --- | --- | --- |
| **Fixed D-pad** | 8-way pad at (left+170, bottom+170), size 260. Sectors: cardinals 50°, diagonals 40° (diagonals easier for QCF/DP). Sliding across sectors changes direction without lifting. | ✅ default |
| Floating stick | LibreQuake-style: origin where thumb lands (left 45 % of screen), radius 110, dead zone 0.25, same 8-sector quantisation. | |
| Fixed stick | Stick at the D-pad position; same quantisation. | |

- The whole left 45 % of the screen (outside buttons) is the direction zone, so a missed
  thumb still steers (mode-dependent).
- Direction output passes through SOCD resolution, then into `InputFrame`.
- Optional **haptic tick** (short vibration) when the direction sector changes.

### 3.2 Right: attack buttons (6 + extras)

Two rows, arcade layout (punches top, kicks bottom), bottom-right corner:

| Button | Logical | Centre (from bottom-right) | Size |
| --- | --- | --- | --- |
| LP | x | (-370, 250) | 130 |
| MP | y | (-225, 280) | 130 |
| HP | z | (-80, 310) | 130 |
| LK | a | (-370, 105) | 130 |
| MK | b | (-225, 135) | 130 |
| HK | c | (-80, 165) | 130 |
| START | s | top-centre (0, -50 from top) | 100×60 |
| PAUSE | m | top-right (-60, -50 from top) | 90 |
| D / W | d / w | hidden by default, toggle in settings | 110 |

- Each button sends its logical input while held (true hold, not tap) — charge moves and
  `$`/`/` commands need holds.
- **Multi-touch:** up to 10 fingers; pressing two buttons at once = simultaneous press.
- **Slide-to-press (option, default on):** a finger sliding from one attack button onto a
  neighbour presses the neighbour too (plinking / `x+y` with one thumb), like arcade apps.
- **Macro buttons (option, default off):** `x+y`, `a+b`, `x+a` shortcut buttons that press
  both logical buttons in the same tick. Never inject special-move sequences in competitive
  modes. (A "simple specials" assist mode may come later as a separate, clearly-labelled
  difficulty option.)
- Labels: localised (AR/EN) short names or icons (fist/foot + size dots); never only colour.

### 3.3 In-game HUD interplay

- Buttons must not cover lifebars, power bars, timer or combo text: keep the top 22 % of
  the screen free except START/PAUSE; global opacity default **0.55**.
- Optional **input display** (training): last 20 inputs as direction arrows + button
  glyphs, like Ikemen's debug input display; also used by the Input Test scene.

### 3.4 Layout editor (Settings → Controls → Edit layout)

- Drag any control, pinch or slider to resize (60 %–160 %), per-control opacity, snap to
  8-unit grid, show safe area and the HUD no-go zone, **Reset to default**, Save / Cancel.
- Three save slots + presets: *Default*, *Left-handed (mirrored)*, *Compact (tablet)*.
- Layout stored in reference units relative to its anchor corner, so it survives
  resolution and aspect changes. Out-of-safe-area or overlapping controls are refused with
  a visible message.

## 4. Gamepad and hardware keyboard

- Use Unity **Input System** package (`com.unity.inputsystem`) for gamepads/keyboards
  (Bluetooth controllers are common on Android). Touch can stay on `Input.touches`
  (as in LibreQuake) or Input System `EnhancedTouch` — pick one and document it.
- Defaults mirror `[Joystick_P1]` / `[Keys_P1]` in `defaultConfig.ini` (table §1).
- When a gamepad or keyboard is used, **auto-hide** on-screen controls; any touch shows
  them again (setting: Auto / Always / Never).
- Remap screen: "press a button for LP…" per logical input, conflict detection, reset.
- Android back button = PAUSE in a match, Back in menus.

## 5. Settings (what, where, defaults)

Stored as **JSON** `settings.json` in `Application.persistentDataPath` with a
`schemaVersion` and migration; write on every change (debounced 0.5 s) and on pause.
Names mirror Ikemen's `config.ini` keys where they exist, so values can be imported/exported
later. Every setting has a default, a range and a unit test for clamp + round-trip.

| Page | Setting | Default | Ikemen key |
| --- | --- | --- | --- |
| **Controls** | Direction mode (D-pad / floating / fixed stick) | D-pad | — |
| | Button size (global) | 100 % | — |
| | Controls opacity | 0.55 | — |
| | Slide-to-press | on | — |
| | Macro buttons | off | — |
| | Show D / W buttons | off | — |
| | Haptics (off / light / strong) | light | `RumbleOn` (gamepad) |
| | On-screen controls (auto / always / never) | auto | — |
| | Edit layout… / Remap gamepad… / Remap keyboard… | — | `[Keys_P1]`, `[Joystick_P1]` |
| | Button assist | on | `Input.ButtonAssist` |
| | SOCD resolution (0–4) | 4 | `Input.SOCDResolution` |
| | Stick sensitivity / dead zone | 0.5 / 0.25 | `ControllerStickSensitivity` |
| **Game** | AI difficulty (1–8) | 5 | `Options.Difficulty` |
| | Life % | 100 | `Options.Life` |
| | Round time (99 / 60 / 30 / ∞) | 99 | `Options.Time` |
| | Rounds to win | 2 | `Options.Match.Wins` |
| | Game speed | 0 | `Options.GameSpeed` |
| | Auto guard | off | `Options.AutoGuard` |
| **Audio** | Master / Music / SFX volume | 100 / 75 / 80 | `MasterVolume`, `BGMVolume`, `WavVolume` |
| **Video** | FPS cap (30 / 60) | 60 | — |
| | Render scale (50–100 %) | 100 % | — |
| | Pixel filtering (sharp / smooth) | sharp | — |
| | Show FPS | off | — |
| **Language** | العربية / English (RTL layout for Arabic menus; numbers stay LTR) | system language | — |
| **About** | Version, credits (CC BY attribution list from `THIRD_PARTY_NOTICES.md`), licences | — | — |

Menu UX: big touch rows (≥ 96 units high), sliders with ± buttons, every change applied
live (preview), "Reset this page", gamepad/keyboard navigable (UI repeat delay/rate from
`UiRepeatDelay = 30`, `UiRepeatRate = 4` frames). Opening Settings in a match pauses it;
leaving waits `PauseExitDelay = 10` ticks so the closing press doesn't hit the character.

## 6. Acceptance tests (must exist and pass before the PR)

Evidence goes to `unity/Builds/validation/<milestone>/` (git-ignored) and is summarised in
`docs/DEV<n>.md`. Be explicit that synthetic tests are **not** physical-device QA.

**EditMode (pure C#, fast):**
1. D-pad quantisation: 360 angles → expected 8 directions (sector table §3.1), dead zone.
2. SOCD modes 0–4 against the Go reference behaviour (table-driven cases).
3. Button assist: press on tick N appears as in Ikemen's `ButtonAssistCheck`.
4. Tick latching: a press+release between two ticks is seen on the next tick.
5. Command recogniser on synthetic touch traces: QCF+x, QCB+y, DP+x, super, `x+y`, `$F,x`
   recognised with `command.time = 15` at 60 Hz (until the real CMD buffer exists in dev.3,
   use a minimal recogniser and replace it later — keep the test cases).
6. Settings: defaults, clamping, JSON round-trip, migration from an older schema.
7. Layout: every preset inside the safe area and non-overlapping at 16:9, 20:9 with a
   punch-hole inset (Poco F3: 2400×1080), 4:3 (tablet), 21:9.

**Rendered (Xvfb + llvmpipe, `-force-glcore`, runner pattern from LibreQuake):**
8. Main menu → Settings → each page → Back; settings changed through UI persist after a
   scene reload.
9. Each on-screen button: synthetic press changes ≥ N pixels in its region and sets the
   right `InputFrame` bit; release clears it; second finger cannot steal a held button;
   focus loss releases all.
10. Layout editor: drag + resize + save + reload restores positions; reset restores default.
11. Labels actually render (pixel check), in both Arabic and English.

**Device:**
12. APK installs and runs on Firebase Test Lab **virtual** `MediumPhone.arm` API 34
    (Robo or instrumentation smoke, screenshots). Never physical FTL.
13. Owner's Poco F3: multi-touch QCF+P, DP, `x+y`, hold-charge, pause/resume, notch area.

## 7. Suggested class layout

```
IK.Input.InputFrame          struct, 14 bools + Vector2 analog, ToBits()/FromBits() like InputBits
IK.Input.InputLogic          pure static: Quantise8, Socd(mode), ButtonAssist — unit-tested
IK.Input.TouchControls       builds left zone + buttons from a ControlLayout, owns fingers
IK.Input.HoldButton          as LibreQuake + slide-to-press support
IK.Input.GamepadInput        Input System actions → InputFrame
IK.Input.InputRouter         60 Hz tick sampler, latching, device auto-hide
IK.Settings.GameSettings     [Serializable] data with defaults
IK.Settings.SettingsStore    load/save/migrate JSON, change events
IK.Settings.ControlLayout    per-control anchor/position/size/opacity + presets
IK.UI.SettingsMenu, LayoutEditor, InputDisplay, UIKit, Skin
IK.Editor.IKUIRegression     rendered UI runner (env IK_UI_FIXTURE=1)
```
