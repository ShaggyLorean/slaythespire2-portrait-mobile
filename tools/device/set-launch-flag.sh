#!/system/bin/sh
# Runs on the device under su. Adds the one-shot game launch flag to the
# launcher preferences without touching the other values, because rewriting the
# whole file changed offline mode and cloud sync between runs.
PKG="com.sts2portrait.mobile.local"
PREFS="/data/data/$PKG/shared_prefs/sts2mobile.xml"
FLAG='    <boolean name="launch_game_on_next_start" value="true" />'

mkdir -p "/data/data/$PKG/shared_prefs"
if [ ! -f "$PREFS" ]; then
    {
        echo "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>"
        echo "<map>"
        echo "$FLAG"
        echo "</map>"
    } > "$PREFS"
else
    grep -v "launch_game_on_next_start" "$PREFS" > "$PREFS.tmp"
    awk -v flag="$FLAG" '{ if ($0 ~ /<\/map>/) print flag; print }' "$PREFS.tmp" > "$PREFS"
    rm -f "$PREFS.tmp"
fi

OWNER=$(stat -c %u:%g "/data/data/$PKG")
chown "$OWNER" "$PREFS"
chmod 660 "$PREFS"

# The Java flag only restarts into the game leg. The launcher still needs to
# know which mode to resume, which is normally set by pressing Play offline and
# then Start game, and the stale "start incomplete" marker would otherwise send
# the run into the recovery path instead of a normal boot.
FILES="/data/data/$PKG/files"
echo "Offline" > "$FILES/pending_launcher_mode_resume"
rm -f "$FILES/last_game_start_incomplete"
chown "$OWNER" "$FILES/pending_launcher_mode_resume"
chmod 660 "$FILES/pending_launcher_mode_resume"
