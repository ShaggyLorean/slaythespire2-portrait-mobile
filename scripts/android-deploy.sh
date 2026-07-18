#!/usr/bin/env bash
# Deploy the portrait mod into a working StS2 mobile launcher install (root, offline).
#
# The launcher's game mod-loader redirect is broken on newer game versions (the "mods"
# string moved into an async state machine), so external mods never load. Instead we
# inject a call to our PortraitMod.Init() into the launcher's own managed entry
# (STS2Mobile.ModEntry.Apply) with Mono.Cecil, and drop our DLL beside it in the
# publish dir. We also NOP the launcher's UiScalePatches (it fights our content scale).
#
# Requirements: rooted device over adb, .NET 9 SDK, our mod built (Release).
# Usage: GAME="/path/to/Slay the Spire 2" scripts/android-deploy.sh
set -euo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; [ -f "$D/local.env" ] && . "$D/local.env"
REPO="$(cd "$D/.." && pwd)"
PKG="${PKG:-com.game.sts2launcher.modmanager}"
PUB="/data/user/0/$PKG/files/.godot/mono/publish/arm64"
GAMEDATA="$GAME/data_sts2_windows_x86_64"
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT

echo "[1/5] Building portrait mod..."
dotnet build "$REPO/src/Sts2Portrait/Sts2Portrait.csproj" -c Release -v q -p:GameDir="$GAME" >/dev/null
MOD="$REPO/src/Sts2Portrait/bin/Release/net9.0/Sts2Portrait.dll"

echo "[2/5] Pulling launcher's STS2Mobile.dll from device..."
adb shell "su -c 'cp $PUB/STS2Mobile.dll /data/local/tmp/STS2Mobile.orig.dll; chmod 644 /data/local/tmp/STS2Mobile.orig.dll'"
adb pull /data/local/tmp/STS2Mobile.orig.dll "$WORK/STS2Mobile.orig.dll" >/dev/null

echo "[3/5] Injecting PortraitMod.Init + disabling UiScalePatches..."
UID_APP="$(adb shell stat -c '%U' "$PUB/STS2Mobile.dll" 2>/dev/null | tr -d '\r')"
dotnet run --project "$REPO/tools/mobile-injector" -c Release -- \
  "$WORK/STS2Mobile.orig.dll" "$MOD" "$WORK/STS2Mobile.patched.dll" \
  "$REPO/tools/mobile-refs;$GAMEDATA;$(dirname "$MOD")" "UiScalePatches"

echo "[4/5] Pushing to device..."
adb push "$WORK/STS2Mobile.patched.dll" /data/local/tmp/STS2Mobile.dll >/dev/null
adb push "$MOD" /data/local/tmp/Sts2Portrait.dll >/dev/null
adb shell "su -c '
  cp /data/local/tmp/STS2Mobile.dll   $PUB/STS2Mobile.dll
  cp /data/local/tmp/Sts2Portrait.dll $PUB/Sts2Portrait.dll
  chown $UID_APP:$UID_APP $PUB/STS2Mobile.dll $PUB/Sts2Portrait.dll
  restorecon $PUB/STS2Mobile.dll $PUB/Sts2Portrait.dll'"

echo "[5/5] Restarting app..."
adb shell am force-stop "$PKG"
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
echo "Done. Tap PLAY — the game boots in portrait."
