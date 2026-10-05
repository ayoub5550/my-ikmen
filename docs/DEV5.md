# dev.5 — the complete game loop: full state controllers, ZSS, CPU AI, screenpack and modes

Status: **feature complete for the shipped content, gates green** (branch `dev.5`, stacked on the
merged dev.4, not merged). Last update: 2026-10-05 13:3x UTC.

dev.4 was one fight between two KFMs and a dummy. dev.5 turns it into **the game**: the app boots
into the Ikemen GO screenpack title screen, the player picks a mode, a character, a colour and a
stage, fights a CPU opponent that plays with its own moves, and goes through VS, victory, continue
and results screens — on touch, gamepad or keyboard. The fight engine gained the rest of the MUGEN
state controllers, Ikemen's ZSS state language and per-character localcoord scaling, so every
character in the roster runs its own files.

The menus and the mode flow are described in detail in `docs/FRONTEND.md`.

---

## 1. Scope of dev.5

| # | Area | Contents | State |
| --- | --- | --- | --- |
| 1 | Trigger redirection | `parent`, `root`, `helper(id)`, `target(id)`, `enemy(n)`, `enemynear(n)`, `playerid(id)`, `p2`, `stateowner`, `helperindex`; `:=` assignment; `map(name)`; `camerapos x`, `hitvel x`; `hitdefattr` lists kept intact | **done** (`Core/Expr.cs`) |
| 2 | Helpers | `Helper`, `DestroySelf`, `ParentVarSet/Add`, `BindToParent/Root/Target`, helpers run their own states and skip the basic actions, `numhelper`, `ishelper` | **done** (`Core/FighterEx.cs`, `Core/FightEngineEx.cs`) |
| 3 | Explods and sparks | `Explod`, `ModifyExplod`, `RemoveExplod`, `GameMakeAnim`, hit / guard sparks from `fightfx` or the character (`S`/`F` prefixes), `numexplod` | **done** |
| 4 | Projectiles | `Projectile` with hits, guard, priority, proj-vs-proj cancel, `projhit`/`projguarded`/`projcontact` triggers | **done** (time comparison approximated, see §3) |
| 5 | Effects | `SuperPause`, `Pause`, `EnvShake`, `FallEnvShake`, `PalFX`, `AllPalFX`, `BGPalFX`, `AfterImage`, `AfterImageTime`, `Trans`, `Angle*`, `Offset`, `SprPriority`, `RemapPal` — drawn with the `UIPalFx` shader | **done** |
| 6 | Hit modifiers | `HitOverride`, `ReversalDef`, `NotHitBy`, `HitBy`, `HitAdd`, `MoveHitReset`, `Target*` controllers, `PosFreeze`, `ScreenBound`, `PlayerPush`, `Width`, `AttackDist` | **done** |
| 7 | Sounds | `PlaySnd`/`StopSnd` with channels, character `.snd`, `common.snd`, `fight.snd` announcer, hit sounds | **done** (no BGM, see §3) |
| 8 | Special states | -4, -3, -2, -1 every tick, as in Go | **done** (`Core/Fighter.cs`) |
| 9 | ZSS | Ikemen's state language: `[StateDef]` headers, `[Function]` (inlined, globals shared across files), `if/else`, `let`, `call`, `persistent`, `ignorehitpause`, sctrl blocks, expression statements, merged negative states; Ikemen's `common1.cns.zss` packed as fallback for common states the native port does not cover | **done** (`Core/ZssFile.cs`; `for`/`while`/`switch` not yet) |
| 10 | Localcoord scaling | per-player `Scl` = stage width / character localcoord width; world-unit push, clamp, guard distance, camera; hit velocities converted attacker → receiver, so `kfm720` fights 320-unit characters at the right size | **done** |
| 11 | CPU AI | learns the attack commands from the `.cmd` -1 state, measures reach from Clsn1, guards, jumps, walks, 8 levels | **done** (`Core/CpuAI.cs`) |
| 12 | Match API | `MatchSetup` / `PlayerSetup` / `MatchResult` / `GameMode`, `FightScreen.StartMatch(setup, onEnd)`, pause menu (Resume / Restart / Boxes / Exit), training life refill | **done** (`Core/MatchSetup.cs`, `UI/FightScreen.cs`) |
| 13 | Screenpack | `system.def` motif: title, select, VS, victory, continue, win, survival results, credits; menu sounds; English bitmap fonts + Arabic | **done** (`docs/FRONTEND.md`) |
| 14 | Modes | Arcade, Versus (vs CPU), Training, Survival, Watch (CPU vs CPU) | **done** (`App/GameFlow.cs`) |
| 15 | Roster | `select.def`: `kfm_zss` (ZSS KFM), `kfm720` (HD KFM), `kfm_zaxis`, `kfm`, random; stages `kfm`, `stage0`, `stage0-720`, `stage1`, `stageZ`, `interactivestage` | **done** (79 files, 23.7 MiB packed) |
| 16 | Gates | compile, EditMode, rendered UI, APK, Firebase virtual device, owner's phone | compile 0 errors · EditMode **250/250** · rendered **163/163** at 1280×720 and **163/163** at 2400×1080 and 1024×768 · APK built (`v0.1.0-dev.5`, 37.2 MB, arm64, code 5) · FTL **not run** (no service account) · phone **not run** |

## 2. How it was verified

- **EditMode (250):** the 202 dev.4 cases unchanged, plus `Dev5EngineTests` (14: redirection,
  helpers, explods, sparks, projectiles, superpause, modifiers, CPU), `ZssTests` (4),
  `MotifTests` (17), `RosterTests` (13).
- **Rendered (163):** the 115 dev.4 checks plus 48 front-end checks (title in English and Arabic,
  select, stage select, VS, victory, continue, survival results, credits, a full menu → fight
  flow). Screenshots in `Builds/validation/ui-<res>/`.
- **Headless harness (outside Unity):** the Core folder compiles with Roslyn against
  `UnityEngine.CoreModule` and runs CPU-vs-CPU matches under Mono in seconds. Used to run
  `kfm_zss` vs `kfm_zss` (full matches with supers and throws, no unknown trigger or controller),
  `kfm` vs `kfm720` (correct relative scale) and `kfm_zaxis`. See AGENTS.md §6.
- **APK:** `aapt dump badging` → `com.ayoub.ikmen`, versionName `0.1.0-dev.5`, versionCode 5,
  minSdk 23, targetSdk 36, `native-code: arm64-v8a`, IL2CPP; debug-signed.

## 3. Known gaps (kept visible instead of being called done)

- ZSS `for` / `while` / `switch` loops are not compiled (warning only; `kfm_zaxis` uses them, so a
  few of its Z-axis helpers do nothing). Unknown in `kfm_zaxis`: `inputTime`,
  `Input.ControllerStickSensitivity`, `GameOption`.
- The Z axis is ignored; 3D stages (`stage3d*`) are excluded from the roster.
- No BGM: the screenpack's music files are not in the repository.
- `ignorehitpause` controllers do not run during hit pause.
- `ProjHit = time` comparisons are approximated (true within 15 ticks of the hit).
- `HitBy`/`NotHitBy` attribute matching uses flags, not exact state/attack pairs.
- The interactive stage's ZSS stage character does not run.
- The dev.4 text announcements still exist as a fallback beside the motif HUD.
- Not implemented: tag / simul / turns teams, story, time attack, netplay, replays, Lua scripts.
- Content is still the placeholder KFM family and Elecbyte fonts — must be replaced before any
  commercial release (README, licences).

## 4. Next (dev.6+)

| Wave | Contents |
| --- | --- |
| dev.6 | ZSS loops, `ignorehitpause` during hit pause, exact attribute pairs, BGM, teams (simul / tag / turns), time attack, more characters and stages from the community packs the owner chooses |
| dev.7 | release: own content, signing with the owner's keystore, performance on the Poco F3, store build (AAB) |
| later | Z axis and 3D stages, Lua, rollback netplay |
