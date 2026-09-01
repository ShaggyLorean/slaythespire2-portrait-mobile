#!/usr/bin/env bash
# Java/manifest-only rebuild: skips dotnet publish, engine staging and BCL
# seeding (all unchanged) and runs gradle alone with the daemon warm, then
# installs. Use scripts/build-android-local.sh when the managed layer, engine
# or assets actually changed.
set -uo pipefail
# Git Bash on Windows rewrites /data/... arguments into C:/Program Files/Git/...;
# adb needs the device paths verbatim (harmless elsewhere).
export MSYS_NO_PATHCONV=1
# Local paths must reach native Windows tools (dotnet, adb) in Windows form;
# on Linux cygpath does not exist and the path passes through unchanged.
winpath() { command -v cygpath >/dev/null 2>&1 && cygpath -w "$1" || printf '%s' "$1"; }
REPO="${STS2_REPO:-$(cd "$(dirname "$0")/../.." && pwd)}"
ADB="${STS2_ADB:-adb}"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
VER="${1:-0.4.0-devfast}"
CODE="${2:-40099}"

for candidate in "${JAVA_HOME:-}" /usr/lib/jvm/java-17-openjdk /usr/lib/jvm/java-17-openjdk-amd64 $(ls -d /usr/lib/jvm/*17* 2>/dev/null | head -1); do
  [ -n "$candidate" ] && [ -x "$candidate/bin/java" ] || continue
  major=$("$candidate/bin/java" -version 2>&1 | head -1 | sed -n 's/.*version "\([0-9][0-9]*\)[."].*/\1/p')
  [ "$major" = "17" ] && export JAVA_HOME="$candidate" && break
done
[ -n "${JAVA_HOME:-}" ] || { echo "no JDK 17 found"; exit 1; }

export ANDROID_HOME="${ANDROID_HOME:-$HOME/Android/Sdk}"
"$REPO/android/gradlew" -p "$REPO/android" assembleMonoRelease \
  -Pexport_version_name="$VER" -Pexport_version_code="$CODE" \
  -Pexport_package_name=com.sts2portrait.mobile.local \
  -Pexport_enabled_abis=arm64-v8a \
  -Prelease_keystore_file="$REPO/tmp/localtest.keystore" \
  -Prelease_keystore_password=android \
  -Prelease_keystore_alias=androiddebugkey 2>&1 | grep -E "BUILD|error:|FAILED" | tail -3

APK=$(ls -t "$REPO"/android/build/outputs/apk/mono/release/*.apk 2>/dev/null | head -1)
[ -z "$APK" ] && { echo "no apk"; exit 1; }
"$ADB" -s "$SERIAL" install -r "$(winpath "$APK")" 2>&1 | tail -1
echo "installed: $(basename "$APK")"
