# Third-party notices / إشعارات الطرف الثالث

This repository vendors upstream resources from the Ikemen GO project.
Their original licence files are kept unchanged next to them.

| Path | Upstream | Version | Licence |
|---|---|---|---|
| `engine/ikemen-go/` | https://github.com/ikemen-engine/Ikemen-GO | tag `v1.0.0` (commit `81c6da71d689625e20db79586815b695da00dd6d`) | MIT (engine + scripts); CC BY 3.0 for the `default-3x5` font and the engine logo — see `engine/ikemen-go/LICENCE.txt` |
| `assets/screenpack/` | https://github.com/ikemen-engine/Ikemen-GO-Screenpack | `master` @ `2d012f14f8fda6a8515879427adcfd422a20333d` | Mixed — see `assets/screenpack/LICENCE.txt` |

## Important licence notes for a commercial release

- **MIT engine code**: free to use, modify and sell. Keep the copyright notice (`engine/ikemen-go/LICENCE.txt`).
- **CC BY 3.0 assets** (screenpack motif, lifebars, sounds, logos, effects, voice lines): usable commercially **with attribution** to the artists listed in `assets/screenpack/LICENCE.txt` (credits screen).
- **Elecbyte M.U.G.E.N font files**: **CC BY-NC 3.0 — non-commercial only**. Must be replaced before any paid / ad-supported release.
- **Kung Fu Man (`chars/kfm*`) and its stage (`stages/kfm.*`)**: Elecbyte sample content with no clear commercial licence. Treat as **placeholder only**; replace with original characters before a commercial release.
  - Since dev.2 a copy of KFM (`kfm.def/.sff/.air/.snd`, ~330 KB) ships **inside the APK** at `unity/Assets/IK/Resources/chars/kfm/` so the loaders have real data to read. It is development/test content: **it must be removed or replaced before any paid or ad-supported release** (dev.7 checklist).
- `.github/` workflows from both upstream repos were intentionally **not** copied (they would run upstream CI in this repo).

## Background music (dev.6)

`unity/Assets/IK/Resources/music/*.ogg` are **original** tracks synthesised by
`tools/music/compose.py` from code (oscillators, noise and a seeded melody generator; no samples
or third-party recordings). They are part of this project and covered by its MIT licence.
