# dev.2 — MUGEN resource loaders (SFF / AIR / DEF / SND) + character viewer

Date: 2026-10-04 · Branch `dev.2` (stacked on `dev.1`) · Unity 2022.3.62f3 LTS

This milestone gives the Unity project the ability to **read real MUGEN character data**.
No fight engine yet (CMD/CNS is dev.3, the fight itself dev.4). The gate is the
*Character viewer* screen: Kung Fu Man is loaded from his original Elecbyte files and
animated at 60 ticks per second exactly as the engine times him.

---

## 1. What was built

| Area | Files (`unity/Assets/IK/Scripts/Core/`) | Notes |
| --- | --- | --- |
| DEF / INI | `MugenDef.cs` | sections, `key = value`, `;` comments, case-insensitive keys, first value wins; MUGEN's forgiving `Atoi`/`Atof`/`IsNumeric` ported from Ikemen GO |
| SFF | `SffFile.cs` | **v1** (PCX blocks, palette-sharing flag, the legacy character 0,0 rule, 0x0C palette marker search) and **v2** (28-byte headers, linked sprites, `lofs`/`tofs` data banks, palette table with links and duplicates) |
| Compression | `SffDecoders.cs` | `RlePcx`, `Rle8`, `Rle5`, `Lz5` — literal ports of `image.go`, including its tolerance for truncated streams |
| PNG sprites | `PngReader.cs` | SFF v2 formats 10/11/12; paletted PNGs keep their **indices** (Unity's `LoadImage` would throw the palette away and break palette swapping) |
| AIR | `AirFile.cs` | elements (`group,number,x,y,time,flip,alpha,xscale,yscale,angle`), `Loopstart`, `Clsn1/Clsn2` + `Default` boxes, `Copy action`, and the playback state machine (`Tick`) ported from `Animation.Action` |
| SND | `SndFile.cs` | Elecbyte `.snd` list + RIFF/WAVE decode (8/16/24/32-bit PCM and 32-bit float) to interleaved floats |
| Character | `MugenCharacter.cs` | reads the `.def`, then the SFF/AIR/SND it names; missing optional files become warnings, not exceptions |
| Platform bridge | `ResourceSources.cs` | `ResourcesSource` (APK) / `FileSource` (editor, tests) + `MugenAssetCache`: indexed pixels + palette → `Texture2D` (RGBA32, point filter, pivot on the MUGEN axis), `SndEntry` → `AudioClip` |
| Viewer UI | `../UI/CharViewer.cs` | action list, play/pause, ×0.25…×2 speed, palette cycling, Clsn box overlay, sound test, live element info |

**Data shipped:** `Assets/IK/Resources/chars/kfm/` holds Elecbyte's Kung Fu Man
(`kfm.def/.sff/.air/.snd`, 330 KB) as `.bytes` TextAssets, because an APK has no readable
file system for assets. A test asserts those copies are byte-identical to
`assets/screenpack/chars/kfm/`. **KFM is placeholder content** and is replaced before any
release (see `THIRD_PARTY_NOTICES.md` and dev.7).

## 2. Evidence (all re-runnable, see TESTING.md)

| Gate | Result |
| --- | --- |
| 1 compile | **0 `error CS`** |
| 2 EditMode | **56 / 56 passed** (`Builds/validation/editmode.xml`) — 17 new cases for the loaders |
| 3 rendered UI | **72 checks, 0 errors** (`Builds/validation/dev2/`), including the viewer screen and the animation timing |
| 4 APK | `com.ayoub.ikmen` 0.1.0 (code 2), arm64-v8a, IL2CPP, minSdk 23 / targetSdk 36 |
| 4 device (virtual) | Firebase Test Lab Robo, `MediumPhone.arm` API 34, Arabic, landscape |
| 5 device (physical) | Poco F3 — **owner gate, not done here** |

### How the decoders are judged

`tools/sff_dump.py` is an **independent Python implementation** of SFF v1/v2 written from
the Go sources. It writes a JSON fixture per file (per-sprite size, offsets, format and a
SHA-1 of the decoded pixels). `MugenLoaderTests` decodes the same files in C# and compares
every entry:

| Fixture | File | Coverage |
| --- | --- | --- |
| `kfm_sff.json` | `chars/kfm/kfm.sff` | SFF v2, **281 sprites**: LZ5 (280) + paletted PNG (1), 16 palettes |
| `kfm_intro_sff.json` | `chars/kfm/intro.sff` | SFF **v1**, 12 PCX sprites with shared palettes |
| `arcade_sff.json` | `font/arcade.sff` | SFF v2 **RLE8**, 67 sprites |
| `fightfx_sff.json` | `data/fightfx.sff` | SFF v2 **PNG**, 311 sprites |
| `decoder_vectors.json` | random streams | RlePcx / Rle8 / **Rle5** / Lz5 on 10 pseudo-random inputs, byte-for-byte |

## 3. Decisions taken in this milestone

1. **Everything is decoded from a `byte[]`, never from a file handle.** Android assets live
   inside the APK, so the loaders take bytes and the platform decides where they come from
   (`IResourceSource`). This also makes every loader testable in EditMode without Unity IO.
2. **Paletted sprites stay paletted.** Pixels keep their palette indices and the palette is
   applied when the texture is built, so dev.5 can do MUGEN palette effects (and the viewer
   already cycles the 16 KFM palettes) instead of baking colours at load time.
3. **Own PNG decoder** for SFF formats 10/11/12 (zlib via `DeflateStream` + the five PNG
   filters). `Texture2D.LoadImage` cannot return palette indices and would silently turn a
   format-10 sprite into a flat RGBA image.
4. **Ports are literal, including the quirks.** `AS` with no digits means alpha 0 in Ikemen,
   the decoders stop advancing on the last byte of a truncated stream, duplicate sprite keys
   keep the first entry. Where MUGEN is forgiving, we are forgiving in the same way.
5. **The viewer draws in localcoord units** (1 MUGEN unit = 3 canvas units, i.e. 240 units =
   the 720-unit canvas height), with the sprite pivot on the MUGEN axis, so element offsets
   and the H/V flip flags move the sprite exactly like the engine does.
6. **KFM's `.air` has 117 live actions, not 120** — actions 44, 45 and 46 are commented out
   with `;` in Elecbyte's file. The first run of the test suite caught this against a wrong
   expectation taken from a naive grep; the fixtures now carry the right number.

## 4. Known limits (do not read these as "done")

- No gameplay: no CMD/CNS, no states, no hit detection. Clsn boxes are **drawn**, not used.
- Sprite scaling/rotation/blend modes from the `.air` are parsed and the H/V flips are
  applied; additive/subtractive blending is not rendered yet (dev.4 renderer).
- Only one character ships (KFM). Stages, screenpack and fonts are read by the same loaders
  but nothing in the UI uses them yet (dev.6).
- `Copy action` is resolved one level deep; chains of copies are not expected in practice.
- SFF v2 format 1 (unused, also unsupported by Ikemen) throws; 24-bit raw sprites are kept
  as raw RGB and converted on texture creation.

## 5. Next (dev.3)

CMD parser + trigger expression evaluator + the CNS state machine (`ChangeState`,
`ChangeAnim`, `VelSet`, `PosAdd`, `HitDef`), so KFM walks, jumps and punches on command.
The loaders of this milestone are the input to that work; the viewer stays as a debug screen.
