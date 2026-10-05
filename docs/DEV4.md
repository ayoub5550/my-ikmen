# dev.4 — the fight: two players, hits, stage, lifebars, rounds

Status: **in progress** (branch `dev.4`, stacked on `dev.3`). This document is written while the
milestone is being built, so the "state" column is the truth at the last update, not a promise.
Last update: 2026-10-05.

dev.3 could *play* one character. dev.4 makes it a **fight**: a second fighter, the hit system, a
real stage with a camera, the lifebars of the screenpack and a round flow from "Round 1, Fight" to
KO and the win pose.

---

## 1. Scope of dev.4 (what "done" means)

| # | Area | Contents | State |
| --- | --- | --- | --- |
| 1 | `HitDef` | full MUGEN 1.0 parameter set: `attr`, `hitflag`, `guardflag`, `animtype`, `priority`, damage, pause times, slide/hit/ctrl times, ground/air/guard velocities, corner push, juggle, `p1stateno`/`p2stateno`, facing, `fall.*`, `getpower`/`givepower`, `hitonce`, `chainid` | **done** (`Core/HitDef.cs`) |
| 2 | `GetHitVars` | the whole `gethitvar(...)` table the hit states read | **done** (`Core/GetHitVars.cs`) |
| 3 | Hit and guard states | 120-155 (guard start / guarding / guard end / guard hit) and 5000-5150 (shake, knock back, air knock, fall, hit ground, bounce, lie down, get up, defeated) | **done** (`Core/HitStates.cs`) |
| 4 | Collision + resolution | Clsn1 × Clsn2 overlap with facing, attribute and hit-flag checks, guard decision, juggle check, damage with attack/defence multipliers, hit pause for both sides, `movecontact` / `movehit` / `moveguarded`, hit counters, power | in progress (`Core/FightEngine.cs`) |
| 5 | Opponent triggers | `p2dist`, `p2bodydist`, `p2statetype`, `p2movetype`, `enemynear`, `numtarget`, `hitdefattr`, `inguarddist`, `hitover`, `hitshakeover`, `hitfall`, `canrecover`, `target*` controllers | in progress |
| 6 | Stage | `stages/*.def` (Info, Camera, PlayerInfo, Bound, StageInfo, Shadow, BGdef, BG blocks, BGCtrl), animated and parallax backgrounds, the Go camera clamp with tension | **done** (`Core/StageFile.cs`, 16 tests) |
| 7 | Lifebars | `data/fight.def`: life/power bars with the slow "mid" bar, face, name, time, combo counter, round / KO / win / draw announcements | **done** (`Core/FightDef.cs`, 18 tests) |
| 8 | Round flow | intro → "Round N" → Fight → fighting → KO / time over → win pose → next round, match over at 2 wins | planned |
| 9 | Fight screen | both fighters drawn on the stage with the camera, lifebars and HUD, driven by the dev.1 touch layer; P2 as a dummy with selectable behaviour (stand / crouch / jump / guard / AI-lite) | planned |
| 10 | Player push | push boxes (`[Size] ground.front/back`), corner push, screen bounds, `width`/`playerpush` | planned |
| 11 | Gates | compile, EditMode (target: ≥ 170 cases), rendered UI checks, APK, Firebase virtual device | partly (143/143 EditMode green with 1-7 in) |

Not in dev.4, by decision: helpers, explods, projectiles, palette effects, AI beyond a dummy,
menus and game modes, 4-player teams, ZSS scripting, rollback netcode. They are the following
waves (§3).

## 2. What is being executed right now

1. `Core/FightEngine.cs`: the per-tick fight loop — input → hit detection → states → physics →
   push and bounds → camera → round state machine → lifebar state.
2. Extending `Core/Fighter.cs` with the opponent-aware triggers and the `Target*` controllers that
   dev.3 left inert.
3. `UI/FightScreen.cs`: drawing the stage, both fighters and the bars, replacing the training
   screen as the default scene for the APK.
4. `Tests/EditMode/HitSystemTests.cs`: KFM vs KFM cases — a punch connects, a blocked punch costs
   guard damage only, an air hit juggles, a knockdown ends in state 5110, a KO ends the round.

## 3. After dev.4 — what is still missing for a complete Ikemen GO on Unity

The owner's goal is the **whole game** on Android in Unity. dev.4 is the fight core; these waves
complete it. Each is a release of its own.

| Wave | Contents | Go reference |
| --- | --- | --- |
| dev.5 | the remaining state controllers: `Helper`, `Explod`, `Projectile`, `PalFX`/`AllPalFX`, `AfterImage`, `EnvShake`, `SuperPause`, `Pause`, `BindTo*`, `Target*`, `HitOverride`, `ReversalDef`, `Dizzy`/`GuardBreak` | `char.go`, `bytecode.go` |
| dev.6 | the screenpack and the modes: `system.def` parsing, title screen, character select, VS, continue, results, arcade / versus / training / survival, `select.def` rosters, multiple characters and stages | `motif.go`, `script.go`, `select_params.go` |
| dev.7 | release: replace the placeholder content (KFM, Elecbyte fonts), audio polish, performance on the owner's device, signing, store build | — |
| later | MUGEN 1.1 extras (zoom, 3D stages), Ikemen extras (ZSS scripting, Lua, tag/simul teams, 4 players), rollback netplay | `stage.go`, `script.go`, `rollback.go` |

**Honest statement of scope:** the Go engine is ~125k lines. dev.1-dev.4 cover the parts a fight
needs; the waves above are what "nothing missing" actually costs. No milestone is reported as done
here without its gates.

## 4. Method (unchanged from dev.1-dev.3)

- Literal port, file by file, with the Go source open; nothing of the Go code runs in the game.
- Every expectation in a test comes from the character's own data files or from the Go reference.
- Gates are separate and named: compile → EditMode → rendered UI → APK → Firebase virtual device →
  the owner's Poco F3. A synthetic test is never reported as device QA.
- Merge only on the owner's explicit word.
