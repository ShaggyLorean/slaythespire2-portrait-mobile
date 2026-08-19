#!/usr/bin/env bash
# Start the game leg deterministically: set the one-shot launch flag straight
# in SharedPreferences and start GodotApp, instead of tapping launcher buttons
# whose position depends on remembered state (that flakiness cost several
# false "it didn't crash" readings).
set -uo pipefail
ADB="/c/Users/whisper/AppData/Local/Android/Sdk/platform-tools/adb.exe"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
PKG="com.sts2portrait.mobile.local"
PREFS="/data/data/$PKG/shared_prefs/sts2mobile.xml"
WAIT="${STS2_WAIT:-25}"

export MSYS_NO_PATHCONV=1
"$ADB" -s "$SERIAL" shell am force-stop "$PKG"
# The flag is set by a script that runs on the device: quoting an in-place edit
# through adb + su mangled the XML and the flag silently never landed.
"$ADB" -s "$SERIAL" push "$(cygpath -w "$(dirname "$0")/set-launch-flag.sh")" /data/local/tmp/set-launch-flag.sh >/dev/null
"$ADB" -s "$SERIAL" shell "su -c 'sh /data/local/tmp/set-launch-flag.sh'"

"$ADB" -s "$SERIAL" logcat -c
"$ADB" -s "$SERIAL" shell am start -n "$PKG/com.game.sts2launcher.LauncherActivity" >/dev/null 2>&1
sleep "$WAIT"
"$ADB" -s "$SERIAL" shell "su -c 'tail -25 /data/data/$PKG/files/sts2_bootstrap_trace.log'" 2>&1 | sed 's/\r//'
