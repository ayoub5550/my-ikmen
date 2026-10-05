# Fist Forge 1.0.0 — packaging release

Fist Forge is the renamed, independently maintained Unity Android project based on
the dev.8 codebase. This is a branding and distribution update, not a new engine
milestone or a claim of complete compatibility with the original Go engine.

- Product: Fist Forge
- Application ID: `com.ayoub.fistforge`
- Version: `1.0.0`, Android version code `1`
- Editor: Unity `2022.3.62f3`
- Android: ARM64, IL2CPP, min API 23, target API 36, landscape
- Demo signature: Android debug certificate; use your own signing key for release

## Changes

New launcher icon, title wordmark, application identity, menu/About branding,
build-menu labels, buyer guide and distribution packaging. Upstream credits and
original licence texts remain included. Gameplay/engine semantics are unchanged.

## Verification

- Unity compile completed successfully.
- EditMode: **275 passed, 0 failed, 0 skipped**.
- Rendered UI fixture: **178 checks, 0 errors** at the 1280×720 Xvfb setting.
  As documented in TESTING.md, this editor fixture uses a 640×480 game view;
  it is not a physical-device resolution test.
- Android APK metadata verified with `aapt`; signature verified with `apksigner`.
- Demo SHA-256:
  `5ae34c677731a09e0fc404b5f911dbfc88c8b039e174e59db05325c829b042b7`.

These are build and software-rendered checks. Physical-device QA for this
rebranded binary is not claimed. Known dev.8 limitations remain, including the
observed state-440 juggle loop and incomplete parity with Go Ikemen.

Read `FIST_FORGE_BUYER_GUIDE.md` before building, reskinning or publishing.
