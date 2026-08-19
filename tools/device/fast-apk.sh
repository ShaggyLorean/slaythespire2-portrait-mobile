#!/usr/bin/env bash
# Java/manifest-only rebuild: skips dotnet publish, engine staging and BCL
# seeding (all unchanged) and runs gradle alone with the daemon warm, then
# installs. ~40 s instead of ~5 min. Use build-android-local.ps1 when the
# managed layer, engine or assets actually changed.
set -uo pipefail
REPO="/d/Projects/slaythespire2-portrait-mobile"
ADB="/c/Users/whisper/AppData/Local/Android/Sdk/platform-tools/adb.exe"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
GRADLE="$REPO/tmp/toolchain/gradle-8.11.1/bin/gradle.bat"
VER="${1:-0.4.0-devfast}"
CODE="${2:-40099}"

export JAVA_HOME="C:/Program Files/Microsoft/jdk-17.0.20.8-hotspot"
"$GRADLE" -p "$REPO/android" assembleMonoRelease \
  -Pexport_version_name="$VER" -Pexport_version_code="$CODE" \
  -Pexport_package_name=com.sts2portrait.mobile.local \
  -Pexport_enabled_abis=arm64-v8a \
  -Prelease_keystore_file="$REPO/tmp/localtest.keystore" \
  -Prelease_keystore_password=localtest \
  -Prelease_keystore_alias=localtest 2>&1 | grep -E "BUILD|error:|FAILED" | tail -3

APK=$(ls -t "$REPO"/android/build/outputs/apk/mono/release/*.apk 2>/dev/null | head -1)
[ -z "$APK" ] && { echo "no apk"; exit 1; }
"$ADB" -s "$SERIAL" install -r "$APK" 2>&1 | tail -1
echo "installed: $(basename "$APK")"
