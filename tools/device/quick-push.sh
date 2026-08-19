#!/usr/bin/env bash
# Fast device iteration: rebuild only the managed layer and push it straight
# into the app's runtime assembly directory, then relaunch the game leg and
# collect the log. Skips the whole APK/gradle chain (5 min -> ~40 s).
# Java, manifest or asset changes still need scripts/build-android-local.ps1.
set -uo pipefail

REPO="/d/Projects/slaythespire2-portrait-mobile"
ADB="/c/Users/whisper/AppData/Local/Android/Sdk/platform-tools/adb.exe"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
PKG="com.sts2portrait.mobile.local"
RUNTIME_DIR="/data/data/$PKG/files/.godot/mono/publish/arm64"
GAME_REF="D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
OUT="$REPO/tmp/device/quick-$(date +%H%M%S).log"

echo "[1/4] building managed layer"
dotnet build "$REPO/src/STS2Mobile/STS2Mobile.csproj" -c Release \
  -p:GameReferenceDir="$GAME_REF" 2>&1 | grep -E "error|Build succeeded" | head -5

DLL=$(ls -t "$REPO"/src/STS2Mobile/bin/Release/net9.0/STS2Mobile.dll 2>/dev/null | head -1)
[ -z "$DLL" ] && { echo "no STS2Mobile.dll built"; exit 1; }

echo "[2/4] pushing STS2Mobile.dll"
"$ADB" -s "$SERIAL" push "$DLL" /data/local/tmp/STS2Mobile.dll >/dev/null
"$ADB" -s "$SERIAL" shell "su -c 'cp /data/local/tmp/STS2Mobile.dll $RUNTIME_DIR/STS2Mobile.dll && chmod 644 $RUNTIME_DIR/STS2Mobile.dll'"

echo "[3/4] launching game leg"
"$ADB" -s "$SERIAL" shell am force-stop "$PKG"
"$ADB" -s "$SERIAL" logcat -c
"$ADB" -s "$SERIAL" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 8
"$ADB" -s "$SERIAL" shell input tap 716 606   # Play offline
sleep 4
"$ADB" -s "$SERIAL" shell input tap 716 626   # Start game
sleep "${STS2_WAIT:-22}"

echo "[4/4] collecting"
"$ADB" -s "$SERIAL" logcat -d > "$OUT" 2>&1
"$ADB" -s "$SERIAL" shell screencap -p /sdcard/quick.png >/dev/null 2>&1
"$ADB" -s "$SERIAL" pull /sdcard/quick.png "$REPO/tmp/device/quick.png" >/dev/null 2>&1
grep -aE "trace\]|patch step|FORTIFY|Fatal|Exception|startup completed|MainMenu" "$OUT" | sed 's/\r//' | tail -20
echo "log: $OUT"
