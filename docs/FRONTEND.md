# dev.5 front end — screenpack menus and game modes

Status: **done for the shipped content, gates green in the front-end copy** (branch `dev.5`). This
file covers the menus and the mode flow; the fight engine work of dev.5 is described in
`docs/DEV5.md`.

## What the player sees

`IKApp` boots into the **title screen** of the screenpack (`data/ikemen1/system.def`), then:

| Screen | Class | Motif source | Notes |
| --- | --- | --- | --- |
| Title | `UI/TitleScreen.cs` | `[Title Info]`, `[TitleBGdef]` | animated sky/clouds/logo (`sin.y`, `velocity`, tiling) via the stage BG renderer; menu right-aligned at `menu.pos` in font 4 (Menu1), active item tinted by `menu.item.active.font`; items: Arcade, VS mode, Training, Survival, Watch, Options, Credits, Exit |
| Select | `UI/SelectScreen.cs` | `[Select Info]`, `[SelectBGdef]` | cell grid with `portrait.spr` 9000,0, random cell, P1/P2 cursor actions 160/170, big portraits = the character's action 0 scaled `face.scale × motif/char localcoord` (Go `motif.go`), names, palette menu (`paletteselect`), opponent, stage, CPU level |
| VS | `UI/VersusScreen.cs` | `[VS Screen]`, `[VersusBGdef]` | both portraits clipped to `pN.window`, names, "Match N", "Next Stage: …", the VS logo action, `time` ticks |
| Victory | `UI/VictoryScreen.cs` | `[Victory Screen]`, `[VictoryBGdef]` | winner left (`p1.spr` 9000,2 → 9000,1 fallback), loser at `p2.lose.brightness`, win quote |
| Continue | `UI/ContinueScreen.cs` | `[Continue Screen]`, `[ContinueBGdef]` | YES/NO, counter action 900 and its voices at `counter.N.skiptime`, NO at `counter.endtime` |
| Results | `UI/ResultsScreen.cs` | `[Win Screen]`, `[Survival Results Screen]` | "Congratulations!", "Rounds survived: N", Game Over |
| Credits | `UI/CreditsScreen.cs` | `[OptionBGdef]` | CC BY 3.0 attribution read from the packed `assets/screenpack/LICENCE.txt`, MIT notice, Amiri OFL, KFM placeholder warning |

Options opens the existing `SettingsMenu`; its new **Developer** button opens the dev.1–dev.4 menu
(`MainMenu`: dummy fight, training room, character viewer, input test).

Everything is touch-first: every item, cell and arrow is a tap target (tap a select cell to move
there, tap it again / the portrait / OK to confirm). D-pad and buttons (gamepad, keyboard, the
router's 60 Hz `InputFrame`s, with auto-repeat) and the Android back key work everywhere. English
labels use the motif's bitmap fonts and its own texts; Arabic labels use the UI font (Amiri) at the
same motif positions and colours.

Menu sounds come from `system.snd` (`cursor.move.snd`, `cursor.done.snd`, `cancel.snd`, palette and
stage sounds, continue voices) through `MugenAssetCache.ClipFor`.

## Code map

- `Core/Motif.cs` — `system.def` loader (pure C#): `[Info]`, `[Files]` fonts, `[Music]`, title,
  select, VS, victory, continue, win, survival; every `[Begin Action]`; `Background(prefix)` turns
  `[<Prefix>BGdef]` + `[<Prefix> …]` blocks into a `StageDefinition` so the existing stage BG code
  draws motif backgrounds. `SearchPathSource` = Ikemen's motif search path.
- `Core/Roster.cs` — `select.def` loader: cells, `randomselect`, `emptyslot`, char params (`order`,
  `music`, `includestage`), `[ExtraStages]`, `arcade/survival.maxmatches`, MUGEN arcade ladder,
  and `Filter` that drops what the port cannot run (3D stages; ZSS characters are allowed since `Roster.ZssSupported = true`).
- `App/GameFlow.cs` — the mode state machine (pure C#).
- `App/FightLauncher.cs` — the only seam to the fight: `FightScreen.StartMatch(MatchSetup,
  Action<MatchResult>)`; `onEnd` is called once after the win pose, or with `Aborted` from the
  pause menu / a failed load (possibly synchronously).
- `UI/MotifAssets.cs` — runtime cache: motif (def parsed at once, `system.sff`/`system.snd`
  decoded on a worker thread, ~4 s in the editor, so the first frame is immediate), fonts, roster,
  character portraits/palettes, menu sound player.
- `UI/MotifView.cs` — motif-space surface (localcoord rect fitted by height, pillarboxed), BG,
  sprite/action/text nodes, tap targets. `UI/FrontEndScreen.cs` — base screen (60 Hz tick, fade-in,
  menu keys).

## Modes

| Mode | Select | Match | After the match |
| --- | --- | --- | --- |
| Arcade | P1 (+ colour) | ladder from `arcade.maxmatches` (6 × order 1 with the shipped roster), CPU level ramps from Options difficulty −1 to +2, opponent's own stage | win → victory → next VS; last win → win screen → title; loss/draw → continue (yes = same opponent, no = game over → title) |
| Versus | P1, CPU opponent, colours, stage (or random), CPU level 1–8 | P2 `AiLevel` = chosen level | victory screen → select |
| Training | P1, dummy, colours, stage | `RoundsToWin 0`, `RoundTime -1`, P2 `AiLevel 0`, no VS screen | exit from pause → select |
| Survival | P1 (+ colour) | one round per match, `P1StartLife` carries over, random opponents, CPU +1 level every 3 wins | first loss → "Rounds survived: N" → title |
| Watch | fighter 1, fighter 2, colours, stage, level | both `AiLevel` = level | victory → select |

Rounds to win / round time come from Options (`GameSettings.roundsToWin`, `roundTime`).

## Roster and content

The game ships `assets/ikmen/select.def` (packed as `Resources/data/select_def.bytes`): `kfm`,
`kfm720`, `randomselect`, and the six 2D stages `kfm`, `stage0`, `stage0-720`, `stage1`, `stageZ`,
`interactivestage`. It is derived from the screenpack's `data/select.def` (kept untouched as the
reference): `kfm_zss` and `kfm_zaxis` are left out because their states are ZSS scripts (set
`Roster.ZssSupported = true` once the engine runs ZSS and add them back), `stage3d*` need 3D models.
`interactivestage`'s attached ZSS character is not run.

`tools/pack_resources.py` now packs 58 files, **22.8 MiB** (motif `system.sff` 8.7 MiB and
`system.snd` 3.5 MiB are most of it). `--check` still verifies the copies.

## Tests

- EditMode: `MotifTests` (17) and `RosterTests` (13, roster + flow) — expectations quoted from
  `system.def`, `select.def` and `tools/sff_dump.py`.
- Rendered (`IKUIRegressionRunner.FrontEnd.cs`, 48 checks): boot into the title, motif art decoded,
  title logo pixels and menu labels in English and Arabic, D-pad navigation + menu sound, select
  portraits in the cells and the big portrait, the whole selection by taps, the `MatchSetup` that
  results, both VS portraits, StartMatch hand-over and pause → Exit back to select, victory winner
  portrait, continue counter + voices, survival text, credits attribution, and one complete
  CPU-vs-CPU match through the flow to the victory screen. Pixel checks diff a capture against the
  same frame with the element hidden. PNGs: `title-English.png`, `title-Arabic.png`, `select.png`,
  `select-stage.png`, `vs.png`, `victory.png`, `continue.png`, `survival-results.png`,
  `credits.png`, `flow-fight.png`.

## Approximations (not hidden)

- The ikemen1 select grid is a perspective trapezoid (`cell.*-N.*` overrides with projection);
  here it is flat, and small rosters get cells up to 3× larger for touch. `face2` perspective side
  portraits, `xshear`, `angle`/projection on texts are not drawn; `Interpolate Scale` in motif
  actions is not interpolated.
- The title menu shows all 8 items (spacing shrinks from 54 to ~53) instead of a 6-item window, so
  nothing needs scrolling on a phone.
- No music: the screenpack's `sound/*.mp3` BGMs are not in the repository/APK.
- Truetype motif fonts (`Open_Sans.def`, the win quote font) fall back to the UI font.
- The continue screen does not play the character's continue states (`p1.state`); the game-over
  storyboard (`gameover.def`), story mode, intros/endings, team modes, time attack, netplay and
  replays are not implemented.
