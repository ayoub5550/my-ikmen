# Fist Forge — Buyer Guide

Fist Forge 1.0.0 (`com.ayoub.fistforge`) is an editable Unity 2022.3.62f3 project for an Android 2D fighting game. Its engine is a C# reimplementation that reads M.U.G.E.N / Ikemen GO style content (`.def/.sff/.air/.cmd/.cns/.snd`, stages, screenpack). Ikemen GO v1.0.0 (Go, MIT) was used only as a behavioural reference. **This is not an official Ikemen GO product, and it is not affiliated with that project.**

## 1. What is included
- Full C# source code under `unity/Assets/IK/` (namespace `IK`): loaders, fight engine, CPU AI, input, settings, UI, and editor build tools.
- Sample content: the packed roster (`Resources/data/select_def.bytes`) has Kung Fu Man variants `kfm`, `kfm720`, `kfm_zss`, `kfm_zaxis` plus random select, and the stages (`kfm`, `stage0`, `stage0-720`, `stage1`, `stageZ`, `interactivestage`), and the Ikemen GO screenpack motif.
- Title-menu modes: Arcade, Team Arcade, VS Mode, Team Versus, Training, Survival, Time Attack, Watch, plus Options and Credits. Team play supports Turns and a simplified Tag mode (2–4 members); team selection is also available in Watch.
- Touch controls (on-screen D-pad and buttons with a layout editor), gamepad and keyboard input, and Android back-key navigation.
- English and Arabic UI. Arabic uses right-to-left layout and shaping.
- Original background music generated from code (`tools/music/compose.py`).
- Upstream licence files and `THIRD_PARTY_NOTICES.md`.

## 2. Requirements and installation
1. Install **Unity 2022.3.62f3** through Unity Hub, with **Android Build Support** (OpenJDK and Android SDK & NDK Tools). Unity 2022 uses **NDK r23b**.
   The project targets API 36, which is newer than the SDK platform Unity 2022 installs by default. Install **Android SDK Platform 36** (and current build-tools) with the SDK Manager / `sdkmanager "platforms;android-36"` into the SDK Unity uses (Preferences → External Tools), or set `IK_TARGET_SDK` to an installed level.
2. In Unity Hub, choose Add → open the `unity/` folder. Do not open the repository root.
3. Switch the platform to Android (File → Build Settings).
4. Run the editor menu **Fist Forge** in this order:
   - `0. Validate project`: compiles the project and checks the scene and fonts.
   - `1. Create scene`
   - `2. Configure player settings`: sets the package id, version, SDK levels and icons.
   - `3. Build Android APK`: writes `Builds/fist-forge.apk`.
   (Older docs call this menu "IKMEN". In the current code it is named **Fist Forge**.)
5. Build settings come from `Assets/IK/Editor/IKBuildPipeline.cs`: IL2CPP, ARM64, minSdk 23 (Android 6.0), targetSdk 36, landscape, ASTC, APK output. You can override them with environment variables: `IK_VERSION`, `IK_VERSION_CODE`, `IK_TARGET_SDK`, `IK_APK`.

The included demo build uses a **debug signature** and is meant for testing only. Before you publish, set up your own keystore and AAB (see §7).

## 3. Adding your own content
The game reads extra content from the app's files folder at runtime:
```
Android/data/com.ayoub.fistforge/files/chars/<name>/<name>.def (+ .sff .air .cmd .cns .snd)
Android/data/com.ayoub.fistforge/files/stages/<stage>.def (+ .sff)
Android/data/com.ayoub.fistforge/files/data/select.def   (optional: replaces the roster)
```
Steps:
1. Install and launch the app once so Android creates the folder.
2. Copy the content folders over USB or with `adb push`.
3. Restart the app. New characters appear after the built-in ones.

If you change the package id, the folder path changes to match the new id. Character files must be unpacked (ZIP archives are not supported). To bundle content inside the APK, see `tools/pack_resources.py` and `unity/Assets/IK/Resources/` (in-APK file names use `_` in place of the dot, for example `kfm_sff.bytes`).

## 4. Project map
| Path | Contents |
|---|---|
| `unity/Assets/IK/Scripts/Core` | SFF/AIR/DEF/SND/CMD/CNS/ZSS loaders, fight engine, AI, motif, roster |
| `unity/Assets/IK/Scripts/Fight`, `Input` | Command recognition, touch, D-pad, gamepad, input router |
| `unity/Assets/IK/Scripts/Settings` | `GameSettings`, `ControlLayout`, JSON `SettingsStore` |
| `unity/Assets/IK/Scripts/UI` | Title, select, VS, victory, continue, results, settings, layout editor, `Skin.cs`, `Loc.cs` |
| `unity/Assets/IK/Scripts/App` | `IKApp`, `GameFlow`, music, benchmark, device probe |
| `unity/Assets/IK/Editor` | `IKBuildPipeline`, regression runners |
| `unity/Assets/IK/Resources` | Packed characters, stages, data, fonts, music, shaders, brand icon |
| `assets/`, `engine/ikemen-go/` | Upstream reference sources (not compiled into the game) |
| `docs/` | Milestone notes (`DEV1–DEV8`), `FRONTEND.md`, `TOUCH_AND_SETTINGS.md` |

## 5. Reskinning
- **Icon:** replace `unity/Assets/IK/Resources/brand/fist-forge-icon.png` (square PNG), then run menu item 2.
- **Title and name:** change `productName` (Player Settings) and the `app.title` entry in `Scripts/UI/Loc.cs`.
- **Package id:** change `PackageName` in `IKBuildPipeline.cs`. Use a different application id for every demo or variant so the installs do not replace each other.
- **Colours:** edit `Scripts/UI/Skin.cs`. **Text:** edit `Scripts/UI/Loc.cs` (Arabic and English pairs). **Screenpack:** edit `data/system.def`.

## 6. Testing status
- The **dev.8** release (`com.ayoub.ikmen`, 0.1.0-dev.8) recorded these results: 0 compile errors, 275/275 EditMode tests, 177 UI regression checks with 0 errors, and Firebase Test Lab robo runs on a virtual device with no crash. See `docs/DEV8.md`.
- The **Fist Forge rebrand (1.0.0)** passed compilation, **275/275 EditMode tests** and **178 rendered UI checks** with no errors. APK identity and signature were checked. See `FIST_FORGE_RELEASE.md` for the exact scope; the UI fixture is not physical-device QA.
- **Physical-device QA (Poco F3) is pending.** Emulator FPS is from a software GPU and does not show real phone performance.

## 7. Known limitations
- This is not the full PC game or content pack. Only the sample roster and stages listed above are included.
- It does not offer universal M.U.G.E.N / Ikemen GO compatibility. Some third-party characters will behave differently or fail to load. 3D stages are filtered out.
- Not implemented: online netplay, Simul mode, story mode, replays, intro/ending storyboards. Tag is simplified: no tag run-in animation (partner appears instantly) and no dedicated team HUD.
- Production signing and Play Store AAB output are not set up. The pipeline builds a debug-signed APK.
- Known engine issue: in AI-vs-AI testing, a fighter could keep the opponent in a state-440 get-hit loop (juggle limits may be missing).
- Screenpack approximations: the select grid is flat, some motif text effects are not drawn, and the continue screen does not play character states.
- The engine has not been compared against Go Ikemen per tick. Expect accuracy differences.

## 8. Third-party obligations
Keep `THIRD_PARTY_NOTICES.md`, `engine/ikemen-go/LICENCE.txt`, `assets/screenpack/LICENCE.txt`, `docs/licenses/*`, and the in-game Credits screen. In particular:
- Ikemen GO code is MIT: keep the copyright notice.
- Screenpack art and sounds are CC BY 3.0: credit the artists listed in `assets/screenpack/LICENCE.txt`.
- Rubik and Amiri fonts are under SIL OFL 1.1.
- The upstream notices describe Kung Fu Man and Elecbyte M.U.G.E.N font files as sample/placeholder content with their own terms. Read those notices yourself and decide whether to replace this content in your released product.
You are responsible for the rights to any content you add.
