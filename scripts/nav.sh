#!/usr/bin/env bash
# Oyun penceresinde navigasyon yardımcıları.
source "$HOME/sts2-portrait/scripts/lib.sh"

nav_wid() {
  local w g W H
  for w in $(DISPLAY=:0 xdotool search --name "Slay the Spire" 2>/dev/null); do
    g=$(DISPLAY=:0 xdotool getwindowgeometry "$w" 2>/dev/null | grep -oE "[0-9]+x[0-9]+" | head -1)
    [ -z "$g" ] && continue
    W=${g%x*}; H=${g#*x}
    if [ "${H:-0}" -gt "${W:-0}" ] 2>/dev/null; then echo "$w"; return; fi
  done
}

# Oran ile tıkla: nav_click <fx 0..1> <fy 0..1>
nav_click() {
  local wid; wid=$(nav_wid)
  [ -z "$wid" ] && { echo "no window"; return 1; }
  local g W H X Y
  g=$(DISPLAY=:0 xdotool getwindowgeometry "$wid" | grep -oE "[0-9]+x[0-9]+" | head -1)
  W=${g%x*}; H=${g#*x}
  X=$(python3 -c "print(int($W*$1))"); Y=$(python3 -c "print(int($H*$2))")
  DISPLAY=:0 xdotool windowactivate "$wid"; sleep 0.6
  DISPLAY=:0 xdotool mousemove --window "$wid" "$X" "$Y" click 1
  echo "clicked ($X,$Y) in $wid"
}

# Ekran görüntüsü: nav_shot <ad.png>
nav_shot() {
  local wid; wid=$(nav_wid)
  [ -z "$wid" ] && { echo "no window"; return 1; }
  DISPLAY=:0 xdotool windowactivate "$wid" 2>/dev/null; sleep 1.2
  DISPLAY=:0 import -window "$wid" "$SCRATCH/$1" 2>/dev/null && \
    magick "$SCRATCH/$1" -resize 380x "$SCRATCH/${1%.png}-s.png" 2>/dev/null && echo "shot $1"
}

# Tuş gönder: nav_key <key>
nav_key() {
  local wid; wid=$(nav_wid)
  DISPLAY=:0 xdotool windowactivate "$wid" 2>/dev/null; sleep 0.4
  DISPLAY=:0 xdotool key --window "$wid" "$1"
}

"$@"
