#!/usr/bin/env bash
# One-shot device probe: relaunch the game leg, wait only until something
# decisive shows up in the log, then print the verdict. Filtered logcat keeps
# the pull under a second instead of dumping the whole ring buffer.
set -uo pipefail
ADB="/c/Users/whisper/AppData/Local/Android/Sdk/platform-tools/adb.exe"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
PKG="com.sts2portrait.mobile.local"
REPO="/d/Projects/slaythespire2-portrait-mobile"

"$ADB" -s "$SERIAL" shell am force-stop "$PKG"
"$ADB" -s "$SERIAL" logcat -c
"$ADB" -s "$SERIAL" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 6
# The launcher shows different first rows depending on remembered state
# (mode picker vs "Start game" vs "Welcome back"), and Godot draws to a
# surface so there is no view tree to query. Walk the candidate rows; the
# buttons are full width, so at most one of these is a real hit per screen.
for y in 605 585 678 700; do
  "$ADB" -s "$SERIAL" shell input tap 719 $y
  sleep 2
done

for i in $(seq 1 20); do
  sleep 2
  OUT=$("$ADB" -s "$SERIAL" logcat -d -s STS2Mobile:* godot:* libc:F AndroidRuntime:E 2>/dev/null | sed 's/\r//')
  if echo "$OUT" | grep -q "FORTIFY\|FATAL"; then echo "$OUT" | tail -12; echo "VERDICT: crashed"; exit 0; fi
  if echo "$OUT" | grep -q "startup completed\|MainMenu\|patch orchestration finished"; then echo "$OUT" | tail -12; echo "VERDICT: booted"; exit 0; fi
done
"$ADB" -s "$SERIAL" logcat -d -s STS2Mobile:* godot:* libc:F | sed 's/\r//' | tail -12
echo "VERDICT: inconclusive"
