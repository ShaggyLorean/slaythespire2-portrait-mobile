#!/usr/bin/env bash
# Tam döngü: build → install → launch → menü bekle → portrait pencereyi bul → screenshot.
# Kullanım: cycle-shot.sh <cikti-adi.png> [ekstra-bekleme-sn]
set -uo pipefail
source "$HOME/sts2-portrait/scripts/lib.sh"
OUT="${1:-shot.png}"
EXTRA="${2:-4}"

sts2_build || exit 1
sts2_install
sts2_kill
sts2_launch "${OUT%.png}.log"
sts2_wait_menu || { echo "MENU FAIL"; exit 1; }
sleep "$EXTRA"

# portrait geometrili pencereyi güvenilir bul
WID=""
for w in $(DISPLAY=:0 xdotool search --name "Slay the Spire 2" 2>/dev/null); do
  g=$(DISPLAY=:0 xdotool getwindowgeometry "$w" 2>/dev/null | grep -oE "[0-9]+x[0-9]+" | head -1)
  case "$g" in *x*) W=${g%x*}; H=${g#*x}; [ "$H" -gt "$W" ] && WID="$w" && break;; esac
done
[ -z "$WID" ] && { echo "NO PORTRAIT WINDOW"; exit 1; }

DISPLAY=:0 xdotool windowactivate "$WID" 2>/dev/null
sleep 2
DISPLAY=:0 import -window "$WID" "$SCRATCH/$OUT" 2>/dev/null && \
  magick "$SCRATCH/$OUT" -resize 380x "$SCRATCH/${OUT%.png}-s.png" 2>/dev/null && \
  echo "SHOT -> $SCRATCH/$OUT (WID=$WID)"
grep -iE "menu layout:" "$GLOG" | tail -1
