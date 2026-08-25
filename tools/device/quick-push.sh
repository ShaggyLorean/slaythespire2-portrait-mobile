#!/usr/bin/env bash
# Fast device iteration: rebuild only the managed layer, drop it in the dev
# override directory the launcher copies from at boot, then start the game leg.
# Pushing straight into the runtime directory does not work: the app re-extracts
# every assembly from the APK on each start, so the push is silently reverted.
# Java, manifest, native or asset changes still need scripts/build-android-local.ps1.
set -uo pipefail

REPO="/mnt/NEVERDELETETHIS/Projects/slaythespire2-portrait-mobile"
ADB="adb"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"

echo "[1/3] building managed layer"
BUILD_LOG="$REPO/tmp/device/build.log"
mkdir -p "$REPO/tmp/device"
dotnet build "$REPO/src/STS2Mobile/STS2Mobile.csproj" -c Release > "$BUILD_LOG" 2>&1
if [ $? -ne 0 ]; then
  grep -E "error" "$BUILD_LOG" | head -5
  echo "build failed, not deploying"
  exit 1
fi

DLL="$REPO/src/STS2Mobile/bin/Release/net9.0/STS2Mobile.dll"
# A green build that left the old binary in place has burned several device
# rounds already, so refuse to deploy anything older than the sources.
NEWEST_SRC=$(ls -t "$REPO"/src/STS2Mobile/*.cs "$REPO"/src/STS2Mobile/**/*.cs 2>/dev/null | head -1)
if [ -n "$NEWEST_SRC" ] && [ "$NEWEST_SRC" -nt "$DLL" ]; then
  echo "stale output: $(basename "$NEWEST_SRC") is newer than STS2Mobile.dll"
  exit 1
fi

echo "[2/3] pushing to override dir"
"$ADB" -s "$SERIAL" shell "mkdir -p /data/local/tmp/sts2_override" >/dev/null 2>&1
"$ADB" -s "$SERIAL" push "$DLL" /data/local/tmp/sts2_override/STS2Mobile.dll
"$ADB" -s "$SERIAL" shell "chmod 644 /data/local/tmp/sts2_override/STS2Mobile.dll"

echo "[3/3] booting game leg"
exec "$REPO/tools/device/boot-game.sh"
