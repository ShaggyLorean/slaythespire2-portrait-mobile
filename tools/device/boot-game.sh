#!/usr/bin/env bash
# Start the game leg deterministically: set the one-shot launch flag straight
# in SharedPreferences and start GodotApp, instead of tapping launcher buttons
# whose position depends on remembered state (that flakiness cost several
# false "it didn't crash" readings).
set -uo pipefail
# Git Bash on Windows rewrites /data/... arguments into C:/Program Files/Git/...;
# adb needs the device paths verbatim (harmless elsewhere).
export MSYS_NO_PATHCONV=1
# Local paths must reach native Windows tools (dotnet, adb) in Windows form;
# on Linux cygpath does not exist and the path passes through unchanged.
winpath() { command -v cygpath >/dev/null 2>&1 && cygpath -w "$1" || printf '%s' "$1"; }
ADB="${STS2_ADB:-adb}"
SERIAL="${STS2_DEVICE:-192.168.1.128:39741}"
PKG="com.sts2portrait.mobile.local"
PREFS="/data/data/$PKG/shared_prefs/sts2mobile.xml"
WAIT="${STS2_WAIT:-25}"

"$ADB" -s "$SERIAL" shell am force-stop "$PKG"
# force-stop returns before the process is gone; starting the activity into a
# dying process drops the intent silently (several deploys "verified" stale
# builds this way). Wait for the pid to clear before launching.
for _ in 1 2 3 4 5 6 7 8 9 10; do
  PID=$("$ADB" -s "$SERIAL" shell pidof "$PKG" 2>/dev/null | tr -d '\r')
  [ -z "$PID" ] && break
  sleep 0.5
done
# The flag is set by a script that runs on the device: quoting an in-place edit
# through adb + su mangled the XML and the flag silently never landed.
"$ADB" -s "$SERIAL" push "$(winpath "$(dirname "$0")/set-launch-flag.sh")" /data/local/tmp/set-launch-flag.sh >/dev/null
"$ADB" -s "$SERIAL" shell "su -c 'sh /data/local/tmp/set-launch-flag.sh'"

"$ADB" -s "$SERIAL" logcat -c
# A launch that silently fails leaves the previous screen up, and every probe
# after it reads the OLD build (a 1.8x change once measured as 1.5x because
# the "new" boot never happened). Count patch orchestrations before and after:
# no new "Applied" line within the wait means the boot did not take.
TRACE="/data/data/$PKG/files/sts2_bootstrap_trace.log"
# The trace rotates and line counts lie; the only stable signal is the ISO
# timestamp each Applied line starts with. Fresh boot = an Applied line
# stamped after the moment we fired the launch intent (device clock, UTC).
START=$("$ADB" -s "$SERIAL" shell date -u +%Y-%m-%dT%H:%M:%S 2>/dev/null | tr -d '\r')
"$ADB" -s "$SERIAL" shell am start -n "$PKG/com.game.sts2launcher.LauncherActivity" >/dev/null 2>&1
# Cold boots write the Applied line anywhere from 12 to 40s in; a fixed sleep
# either wastes time or cries wolf. Poll until the orchestration lands.
DEADLINE=$((WAIT * 2))
ELAPSED=0
FRESH=0
while [ "$ELAPSED" -lt "$DEADLINE" ]; do
  sleep 3
  ELAPSED=$((ELAPSED + 3))
  AFTER=$("$ADB" -s "$SERIAL" shell "su -c 'grep \"Applied .*layout patch classes\" $TRACE | tail -1'" 2>/dev/null | tr -d '\r')
  TS=${AFTER%% *}
  if [ -n "$TS" ] && [ -n "$START" ] && [ "$TS" \> "$START" ]; then
    FRESH=1
    echo "fresh patch orchestration after ${ELAPSED}s"
    break
  fi
done
if [ "$FRESH" -eq 0 ]; then
  echo "WARNING: no fresh patch orchestration within ${DEADLINE}s; the game leg did NOT boot (old build still on screen)" >&2
fi
# Give the menu a few seconds to settle past the orchestration.
sleep 8
"$ADB" -s "$SERIAL" shell "su -c 'tail -25 $TRACE'" 2>&1 | sed 's/\r//'
