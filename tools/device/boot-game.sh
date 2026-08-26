#!/usr/bin/env bash
# Start the game leg deterministically: set the one-shot launch flag straight
# in SharedPreferences and start GodotApp, instead of tapping launcher buttons
# whose position depends on remembered state (that flakiness cost several
# false "it didn't crash" readings).
set -uo pipefail
ADB="adb"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
PKG="com.sts2portrait.mobile.local"
PREFS="/data/data/$PKG/shared_prefs/sts2mobile.xml"
WAIT="${STS2_WAIT:-25}"

"$ADB" -s "$SERIAL" shell am force-stop "$PKG"
# The flag is set by a script that runs on the device: quoting an in-place edit
# through adb + su mangled the XML and the flag silently never landed.
"$ADB" -s "$SERIAL" push "$(dirname "$0")/set-launch-flag.sh" /data/local/tmp/set-launch-flag.sh >/dev/null
"$ADB" -s "$SERIAL" shell "su -c 'sh /data/local/tmp/set-launch-flag.sh'"

"$ADB" -s "$SERIAL" logcat -c
# A launch that silently fails leaves the previous screen up, and every probe
# after it reads the OLD build (a 1.8x change once measured as 1.5x because
# the "new" boot never happened). Count patch orchestrations before and after:
# no new "Applied" line within the wait means the boot did not take.
TRACE="/data/data/$PKG/files/sts2_bootstrap_trace.log"
BEFORE=$("$ADB" -s "$SERIAL" shell "su -c 'grep -c \"Applied .*layout patch classes\" $TRACE'" 2>/dev/null | tr -d '\r')
"$ADB" -s "$SERIAL" shell am start -n "$PKG/com.game.sts2launcher.LauncherActivity" >/dev/null 2>&1
sleep "$WAIT"
AFTER=$("$ADB" -s "$SERIAL" shell "su -c 'grep -c \"Applied .*layout patch classes\" $TRACE'" 2>/dev/null | tr -d '\r')
if [ -n "$BEFORE" ] && [ -n "$AFTER" ] && [ "$AFTER" -le "$BEFORE" ] 2>/dev/null; then
  echo "WARNING: no fresh patch orchestration in trace; the game leg did NOT boot (old build still on screen)" >&2
fi
"$ADB" -s "$SERIAL" shell "su -c 'tail -25 $TRACE'" 2>&1 | sed 's/\r//'
