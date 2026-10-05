# dev.3 — the fight engine in C#: expressions, commands, states, training screen

Date: 2026-10-05 · Branch `dev.3` (stacked on `dev.2`) · Unity 2022.3.62f3 LTS

dev.2 could *read* a MUGEN character. dev.3 **runs** one: the `.cmd` command system, the
trigger expression language, the `.cns` state machine and the standard common states are
re-implemented in C#, and the *Training* screen lets you play Kung Fu Man with the dev.1
touch controls at a fixed 60 ticks per second. No opponent yet — `HitDef` is parsed,
counted and reported, but nothing is hit (that is dev.4).

Nothing of the Go engine runs in the game (ADR-001). Every file below is a hand-written
port of a named Go source, kept close enough to the original that a behaviour question can
be answered by opening both files side by side.

---

## 1. What was built

| Area | File (`unity/Assets/IK/Scripts/Core/`) | Ported from | Notes |
| --- | --- | --- | --- |
| Expressions | `Expr.cs` | `compiler.go` triggers | full operator set (`\|\| ^^ && \| ^ & = != < <= > >= + - * / % **`, unary `- ! ~`), MUGEN range forms `= [a,b)`, tuples (`attr = S, NA`), the function library (`abs floor ceil min max sin cos tan asin acos atan exp ln log sign fmod ifelse cond random`), `const(...)`, and **`AnimElem = n [, op m]`** as a comparison on `AnimElemTime(n)`. An unknown name evaluates to 0 and is recorded instead of throwing. |
| Commands | `CmdFile.cs` | `input.go` | `CommandStepKey` (`~ / $ >` + charge times), `CommandStep`, `MugenCommand` with Ikemen's per-step `completed[]` / `stepTimers[]` model, `AutoGreaterExpand` (`F, F` → `F, >~F, >F`), the `IsDirToButton` evaluation order, `ApplyBackwardCompatibility`, and `GreaterCheckFail`. |
| Input buffer | `CmdFile.cs` (`InputBuffer`) | `input.go` `InputBuffer` | per-key tick counters (positive = held, negative = released) and the full `State` / `StateCharge` tables, including the conflicting-direction rules: plain `D` is false while `DF` is held, `$D` is true. |
| States | `CnsFile.cs` | `compiler.go` | `[Statedef n]` + `[State n, label]`, `triggerall` / `trigger1..N` (AND inside a group, OR between groups), `persistent`, `ignorehitpause`; expressions are compiled at load time. |
| Constants | `CharConstants.cs` | `char.go` `CharGlobalInfo` | `[Data] [Size] [Velocity] [Movement]`, including `runjump.*` and the friction thresholds. |
| Character engine | `Fighter.cs` | `char.go` | the per-tick loop (hard-coded keys → state −1 → current state → physics → animation), 30+ state controllers, the trigger table, `AssertSpecial` flags, `posUpdate` order (move first, then friction/gravity) and the engine's own landing rule. |
| Common states | `CommonStates.cs` | `data/common1.cns.zss` + `char.go` `actionPrepare` | states 0, 10, 11, 12, 20, 40, 45, 50, 51, 52, 100, 105, 106 with the real animations (jumping is 41/42/43, landing is 47 — **not** the state numbers) and the hard-coded walk / crouch / jump / air-jump / brake transitions. |
| Animation | `AirFile.cs` | `anim.go` | added `AnimTime` and `AnimElemTime(n)`, which the triggers above need. |
| Training screen | `../UI/TrainingScreen.cs` | — | drives a `Fighter` from the dev.1 `InputRouter` at 60 Hz, draws the current element on the MUGEN axis with facing, shows Clsn boxes, plays `PlaySnd` sounds, and prints a HUD: state, state time, anim, element, ctrl, velocity, position, power, HitDef count and the last matched command. |

Why the common states are C# and not a file: Elecbyte's `common1.cns` is not
redistributable and Ikemen replaced it with its own ZSS script, a different language. The
states are therefore implemented natively **with the same numbers, animations, velocities
and timings**, and a character that defines one of them in its own `.cns` still wins (KFM
defines none below 110).

**Data shipped:** `kfm_cmd.bytes` and `kfm_cns.bytes` join the dev.2 files in
`Assets/IK/Resources/chars/kfm/`; a test asserts all six copies are byte-identical to
`assets/screenpack/chars/kfm/`. KFM remains placeholder content (dev.7).

## 2. Evidence (all re-runnable, see TESTING.md)

| Gate | Result |
| --- | --- |
| 1 compile | **0 `error CS`** |
| 2 EditMode | **110 / 110 passed** (`Builds/validation/editmode.xml`) — 54 new cases for the engine |
| 3 rendered UI | **97 checks, 0 errors** (`Builds/validation/dev3/`), 25 of them on the training screen |
| 4 APK | `com.ayoub.ikmen` 0.1.0 (code 3), arm64-v8a, IL2CPP, minSdk 23 / targetSdk 36 |
| 4 device (virtual) | Firebase Test Lab Robo — see §5 |
| 5 device (physical) | Poco F3 — **owner gate, not done here** |

### What the new tests actually assert

Every expected number comes from the character's own files or from the Go reference, never
from "what the code happened to print":

| Area | Examples |
| --- | --- |
| Expressions | precedence (`2 + 3 * 4` = 14), right-associative `**`, `5 = [1,5]` vs `5 = [1,5)`, division by zero = 0, unknown trigger = 0 **and reported**, `AnimElem = 3` ≡ `AnimElemTime(3) = 0` |
| .cmd parsing | 37 command blocks / 34 distinct names, `~D, DF, F, x` → 4 steps with the diagonal as one key, `F, F` → 3 steps with `>`, `/$D` → slash+dollar, `~30$B` → charge 30, hold-only commands forced to `buffer.time = 1`, per-command `time` values (1 / 3 / 10 / 15) |
| Command matching | the quarter-circle fires, the mirrored motion does not, a motion slower than `time` is refused, double tap vs single tap, `$D` matches `DF` while plain `D` does not, a 30-tick charge is required, a button command is true only on the press tick |
| .cns parsing | 58 states + `[Statedef -1]`, statedef 200's eight parameters, its three controllers in file order with `damage = 23, 0`, `[State -1, Smash Kung Fu Upper]`'s 3 `triggerall` lines and 3 trigger groups |
| State machine | walk 2.4 units/tick forward and −2.2 back with anims 20/21, crouch 10→11→12→0, jump start of 3 ticks then `jump.neu y = −8.4`, apex ≈ 84 units, landing exactly on y = 0, forward jump 2.5, exactly one air jump (`airjump.num = 1`) at `airjump.height = 35`, run 4.6/tick that the hard-coded walk cannot interrupt, back hop at −4.5/−3.8, state 200 with `poweradd = 10`, its `PlaySnd` on `Time = 1` and its single `HitDef` on `AnimElem = 3`, crouching punch 400, taunt 195, `QCF + x` → 1000, the super 3000 only with `power >= 1000` |
| Soak | 20 000 ticks of random input: no exception, no NaN, >20 states visited, **zero unknown triggers and zero unrecognised controllers** |
| Rendered | the fighter is drawn, walking/punching/jumping change the pixels, Clsn boxes are drawn, the HUD text really renders, `QCF + x` reaches state 1000 through the *input frames* the touch layer produces |

## 3. Decisions taken in this milestone

1. **Hand port, not a transpiler.** `go2cs` converts Go to C# but targets **.NET 10**;
   Unity 2022.3 is Mono / .NET Standard 2.1, so its output cannot compile here, and its
   style (goroutines, pointer emulation) would be unmaintainable. The port is manual and
   function-by-function, and fidelity is proven by tests against the original data files.
   A measured `go2cs` experiment stays on the list as a *measurement*, not as a plan.
2. **Ikemen's command model, not a simpler one.** The first draft matched "one step per
   tick" and could not recognise `~D, DF, F, x` at human speed. The engine now keeps a
   completion flag and timer per step and walks them in Ikemen's evaluation order, which
   is why a quarter circle followed by a button on the same tick works.
3. **Physics order is `posUpdate`'s order:** the position moves with the velocity the state
   just set, and only then friction or gravity are applied. This is why walking covers
   exactly `walk.fwd` units per tick while `vel x` reads `walk.fwd × friction` between ticks.
4. **The engine's own transitions live outside the states.** Walking, crouching, jumping,
   braking and landing are hard-coded in `char.go`, not in `common1.cns`; they are therefore
   in `CommonStates.BasicActions` and in `Fighter.ApplyPhysics`, gated by `AssertSpecial`
   flags (state 100 asserts `noWalk`, which is what keeps a run from decaying into a walk).
5. **Unknown is reported, never silent.** An unknown trigger evaluates to 0 and lands in
   `Fighter.UnknownTriggers`; an unrecognised controller lands in `UnknownControllers`.
   Controllers that need the opponent (`HitOverride`, `ReversalDef`, `SuperPause`, the
   `Target*` family…) are accepted and inert, and listed as such in the code.

## 4. Known limits (do not read these as "done")

- **No opponent, no hit detection, no damage.** `HitDef` is parsed and counted only. Hit
  states (5000+), guarding, juggling, combos and the lifebars are dev.4.
- **No stage.** The training screen clamps the character to ±220 units and has no camera,
  no background and no corner push.
- **No helpers, explods, projectiles or palette effects** — those controllers are inert.
- `Turn` / auto-turn (state 5) is not implemented: with no opponent there is nothing to
  face. The facing flag exists and the command system already uses it.
- The engine runs **one** fighter; `Fighter` has no notion of teams or players yet.
- Physics uses a fixed snap threshold of 1 unit for standing friction (Ikemen derives it
  from the stage/localcoord ratio, which needs the stage — dev.4).

## 5. Firebase Test Lab

The virtual-device Robo run for this build is reported in the release notes of
`v0.1.0-dev.3` and in PR #4. When the daily free quota is exhausted the run is **not**
faked or skipped: it is reported as missing and retried, exactly as in dev.2.

## 6. Next (dev.4)

1. A second fighter and the hit system: `HitDef` → `GetHitVars` → hit states 5000-5100,
   guarding, hit pause, juggling, `movecontact` / `movehit` / `hitdefattr` triggers.
2. A real stage (`stages/*.def`), camera, corner push, screen bounds.
3. Lifebars and the power bar from the screenpack (`data/fight.def`).
4. Then dev.5 (AI + gamepad polish), dev.6 (menus/Arabic), dev.7 (release content).
