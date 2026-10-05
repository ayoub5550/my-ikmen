# dev.4 — the fight: two players, hits, stage, lifebars, rounds

Status: **feature complete, gates green** (branch `dev.4`, stacked on `dev.3`, not merged). The
"state" column is the truth at the last update, not a promise. Last update: 2026-10-05 03:1x UTC.

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
| 4 | Collision + resolution | Clsn1 × Clsn2 overlap with facing, attribute and hit-flag checks, guard decision, juggle check, damage with attack/defence multipliers, hit pause for both sides, `movecontact` / `movehit` / `moveguarded`, hit counters, power | **done** (`Core/FightEngine.cs`, 24 tests) |
| 5 | Opponent triggers | `p2dist`, `p2bodydist`, `p2statetype`, `p2movetype`, `enemynear`, `numtarget`, `hitdefattr`, `inguarddist`, `hitover`, `hitshakeover`, `hitfall`, `canrecover`, `target*` controllers | **done** (`Core/Fighter.cs`) |
| 6 | Stage | `stages/*.def` (Info, Camera, PlayerInfo, Bound, StageInfo, Shadow, BGdef, BG blocks, BGCtrl), animated and parallax backgrounds, the Go camera clamp with tension | **done** (`Core/StageFile.cs`, 16 tests) |
| 7 | Lifebars | `data/fight.def`: life/power bars with the slow "mid" bar, face, name, time, combo counter, round / KO / win / draw announcements | **done** (`Core/FightDef.cs`, 18 tests) |
| 8 | Round flow | intro → "Round N" → Fight → fighting → KO / time over → win pose → next round, match over at 2 wins | **done** (`Core/FightEngine.cs`) |
| 9 | Fight screen | both fighters drawn on the stage with the camera, lifebars and HUD, driven by the dev.1 touch layer; P2 as a dummy with selectable behaviour (stand / guard / jump / walk) | **done** (`UI/FightScreen.cs`, `UI/StageRenderer.cs`) |
| 10 | Player push | push boxes (`[Size] ground.front/back`), corner push, screen bounds, `width`/`playerpush` | **done** (`Core/FightEngine.cs`) |
| 11 | Game data inside the APK | the stage, `data/fight.def`, `fight.sff/snd`, `fightfx`, `glyphs` and the ten `ikemen1` fonts packed into `Resources` (`tools/pack_resources.py`) | **done** (8.8 MiB, 33 files) |
| 12 | HUD from the motif | `data/fight.def` + `fight.sff`: life/power bars with their clip ranges, the timer in the motif's bitmap font, round / fight / KO / time-over / winner announcements, win icons, names | **done** (`UI/FightHud.cs`, 21 tests) |
| 13 | Gates | compile, EditMode, rendered UI checks, APK, Firebase virtual device, owner's phone | compile 0 errors · EditMode **202/202** · rendered **115/115** · APK built (`v0.1.0-dev.4`) · FTL **not run** (no service account) · phone **not run** |

Not in dev.4, by decision: helpers, explods, projectiles, palette effects, AI beyond a dummy,
menus and game modes, 4-player teams, ZSS scripting, rollback netcode. They are the following
waves (§3).

## 2. What the fight screen does today

Menu → **Fight** starts KFM vs KFM on the KFM stage, 60 ticks per second, player 1 on the dev.1
touch layer and player 2 as a dummy (stand / guard / jump / walk, cycled with the Dummy button):

1. the stage is drawn from `stages/kfm.def` + `kfm.sff`: seven BG elements, per-layer
   `delta` scrolling, infinite tiling of the wall and pillars, the additive floor reflection and
   the parallax floor and ceiling; the floor line is the stage's own `zoffset`;
2. both fighters run their real `.cns` states and `.cmd` commands, hits connect through
   Clsn1 × Clsn2 with guard, damage (23 for the KFM light punch), hit pause, corner push and
   juggle, and the round runs "Round 1" → "Fight" → KO → win pose → next round;
3. the life / mid / power bars are driven by `LifeBarState` from `data/fight.def`, the timer by
   `framespercount = 60`, and the announcements by the round state machine.

Known gaps kept visible instead of being called done:

- HUD: no `xshear`, no `palfx` at all (`FightDef` does not parse it), no red life, no guard or
  stun bar, no faces, no combo counter, only the normal win icon, no fades, no HUD sounds,
  1v1 only — the project's own widgets stay as a fallback when the motif fails to load;
- `BGCtrl` blocks are parsed but not executed, and stage zoom, screen shake, `window`/`maskwindow`
  deltas, element PalFX, rotation / shear / projection and stage models are not drawn;
- `trans = sub` has no uGUI equivalent and is approximated with half transparency;
- `tile = n > 1` draws n copies forward rather than MUGEN's exact anchor;
- no hit sparks, no sound for the announcer (`fight.snd` is in the APK, not yet wired).

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
- New since dev.4: the Go engine is kept as a **test oracle** (`rollback.go` has
  `RecordReplayFrame`, `SaveGameState`, `Checksum`), so a headless Go trace can be diffed against
  the C# engine tick by tick. Harness planned under `tools/` in dev.5.
