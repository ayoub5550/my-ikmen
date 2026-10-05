#!/usr/bin/env bash
# Builds the complete Ikemen GO engine as an Android APK, without Docker.
#
# Upstream only documents the Docker path (build/build_android.sh). This script does the
# same thing with user-space tools, which is what a sandbox without root can run. It was
# used on 2026-10-04 to produce a working 57.6 MB APK (org.ikemen_engine.ikemen_go,
# arm64-v8a) containing the whole engine plus the screenpack — see docs/NATIVE-ANDROID.md.
#
# Inputs (override with env vars):
#   ENGINE_SRC   a writable copy of engine/ikemen-go   (the build writes into it)
#   NDK          Android NDK r27d
#   SDK          Android SDK with platform 34 + build-tools 34.0.0
#   JDK17        JDK 17 (Gradle needs 17, Unity's JDK 11 will not do)
#   GOROOT_DIR   Go 1.20.x toolchain
# Needs on PATH: cmake, ninja, nasm, yasm, pkg-config, git, unzip, make.
# Network: clones SDL2, libxmp, libvpx, FFmpeg 7.1 and the ikemen-droid wrapper.
set -euo pipefail

ENGINE_SRC="${ENGINE_SRC:-/work/native/ikemen-go}"
NDK="${NDK:-/work/native/android-ndk-r27d}"
SDK="${SDK:-/work/unity/android-sdk}"
JDK17="${JDK17:-/work/unity/jdk17}"
GOROOT_DIR="${GOROOT_DIR:-/work/native/go}"
EXTRA_BIN="${EXTRA_BIN:-/work/tools-bin/sys/usr/bin}"
EXTRA_LIB="${EXTRA_LIB:-/work/tools-bin/sys/usr/lib/x86_64-linux-gnu}"

export PATH="$GOROOT_DIR/bin:$EXTRA_BIN:$JDK17/bin:$PATH"
export LD_LIBRARY_PATH="$EXTRA_LIB:${LD_LIBRARY_PATH:-}"
export ANDROID_NDK_HOME="$NDK"
export ANDROID_SDK_ROOT="$SDK"
export ANDROID_HOME="$SDK"
export JAVA_HOME="$JDK17"
export GOMODCACHE="${GOMODCACHE:-/work/native/gomod}"
export GOCACHE="${GOCACHE:-/work/native/gocache}"
export GOFLAGS=-modcacherw
export CGO_ENABLED=1
export GOEXPERIMENT=arenas
export APP_VERSION="${APP_VERSION:-ayoub-dev}"
export APP_BUILDTIME="${APP_BUILDTIME:-$(date '+%Y.%m.%d')}"
export MAKEFLAGS="-j$(env -u OMP_NUM_THREADS -u OMP_THREAD_LIMIT nproc)"

for tool in cmake ninja nasm yasm pkg-config git unzip make; do
  command -v "$tool" >/dev/null || { echo "missing tool: $tool" >&2; exit 1; }
done
[ -d "$NDK" ] || { echo "missing NDK at $NDK" >&2; exit 1; }
[ -x "$GOROOT_DIR/bin/go" ] || { echo "missing Go at $GOROOT_DIR" >&2; exit 1; }

cd "$ENGINE_SRC"
go env -w GO111MODULE=on
go mod download
./build/build.sh Android

echo "==> outputs"
ls -lh bin/ lib/
# bin/ikemen-go.apk   the installable game
# bin/libmain.so      the engine itself (c-shared, arm64)
# lib/*.so            SDL2, FFmpeg, libxmp runtime libraries
