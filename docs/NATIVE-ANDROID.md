# The complete game on Android, today — building the engine natively

Date: 2026-10-04 · Branch `dev.3` · Result: **a working 57.6 MB APK with the whole engine**

The owner asked for "the complete game", not another milestone. This document records the
answer, the evidence, and the three strategies that were on the table.

---

## 1. The finding

Ikemen GO **already supports Android upstream**: `build/build_android.sh`, a GLES 3.2
render path (`src/render_gles32.go`, `src/font_gles32.go`), an Android entry point
(`src/util_android.go`) and a Gradle wrapper project (`ikemen-droid`). Upstream only
documents the Docker route; this sandbox has no Docker, so
`tools/sandbox/build_engine_android.sh` reproduces it with user-space tools.

## 2. What was built

| | |
| --- | --- |
| APK | `bin/ikemen-go.apk`, **57,600,392 B (54.9 MiB)** |
| Package | `org.ikemen_engine.ikemen_go`, label *I.K.E.M.E.N-Go*, versionName 1.0 |
| ABI | **arm64-v8a**, minSdk 21, targetSdk 30 |
| Engine | `bin/libmain.so` 13.5 MB (Go `-buildmode=c-shared`, tags `android,gles2`) |
| Runtime libs | libSDL2, libxmp, libavcodec/format/util/filter/device, libswresample, libswscale |
| Assets inside | 56 character files (KFM and its variants), 25 stage files, the full screenpack, fonts, videos — 80 MB uncompressed, 250 entries |
| Gameplay | everything the desktop engine has: CNS/CMD state machine, AI, arcade/training/versus, netplay, Lua/ZSS scripting |

Build chain that ran: libvpx → FFmpeg 7.1 → SDL2 → libxmp → `go build -buildmode=c-shared`
→ Gradle `assembleDebug`. Total ≈ 12 minutes on 17 cores.

### Reproduce

```sh
ENGINE_SRC=/path/to/writable/copy/of/engine/ikemen-go \
NDK=/path/to/android-ndk-r27d SDK=/path/to/android-sdk JDK17=/path/to/jdk17 \
GOROOT_DIR=/path/to/go1.20 tools/sandbox/build_engine_android.sh
```

Requirements: Go 1.20.x, NDK **r27d** (the build targets `aarch64-linux-android34`, older
NDKs have no API-34 clang wrapper), Android SDK platform 34 + build-tools 34.0.0, JDK 17
for Gradle (Unity's JDK 11 fails), plus cmake, ninja, nasm, yasm, pkg-config.

## 3. The three strategies, compared honestly

| | A. Native engine build | B. Automatic Go→C# translation | C. Hand port to Unity (dev.1-dev.7) |
| --- | --- | --- | --- |
| How | compile the real Go engine for Android | [go2cs](https://go2cs.net/) converts the Go sources | rewrite the engine in C# milestone by milestone |
| Completeness | 100 %, it *is* the engine | logic yes, platform layer no (cgo: SDL2, GLES, FFmpeg) | whatever has been written so far |
| Effort | hours (done) | weeks + unknowns | months |
| Blocker | the on-screen controls are the upstream wrapper's | go2cs output targets **.NET 10**; Unity 2022 is Mono/.NET Standard 2.1 | time |
| Workaround | rebuild with the `ikemen-droid Pro` wrapper (dynamic touch gamepad, native controller support) | ship as a **.NET 10 Android** app, or wait for Unity 6.8 (CoreCLR + .NET 10); replace the cgo layer with **Silk.NET** / **SDL3-CS** bindings, which have Android support | — |
| Ownership / sale | upstream code and content, MIT engine + non-commercial art | converted code, still upstream's design | fully ours, sellable |

**Decision:** keep both tracks. A gives the owner a complete, playable game now; C (the
Unity project) stays the product we own, control and can sell, and keeps the Arabic UI and
the touch layer that the wrapper does not have.

## 4. Licence reminder

The engine is MIT, but the bundled content is not all commercial-friendly: Elecbyte's KFM
and the MUGEN fonts are **non-commercial / unclear**, the screenpack art is CC BY 3.0 with
attribution. This APK is for **testing on the owner's device only** — see
`THIRD_PARTY_NOTICES.md`.

## 5. Open items

- Device test on the Poco F3 (owner gate): does it boot, what is the frame rate, how do the
  upstream on-screen controls feel?
- If the controls are poor: rebuild with `ANDROID_APK_REPO=https://github.com/satan-1412/ikemen-droid`
  (the "Pro" wrapper: dynamic virtual gamepad, native HID controller support, armv7 + arm64).
- Shrink: the APK carries every bundled character, stage and video; an Arabic-only build
  with one stage would be far smaller.
- A measured go2cs experiment (how many engine files convert and compile unchanged) before
  anyone bets on strategy B.
