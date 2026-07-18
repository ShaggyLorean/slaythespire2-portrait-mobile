#!/usr/bin/env bash
# Menüden yeni bir run başlatıp haritaya ulaşır (IRONCLAD). Otonom navigasyon.
source "$HOME/sts2-portrait/scripts/lib.sh"
M="$HOME/sts2-portrait/scripts/mcp.sh"

echo ">> singleplayer"; bash "$M" sel singleplayer >/dev/null; sleep 1
# run modu (bazı profillerde çıkar)
bash "$M" sel standard >/dev/null 2>&1; sleep 1
echo ">> IRONCLAD"; bash "$M" sel IRONCLAD >/dev/null; sleep 1
echo ">> embark"; bash "$M" sel embark >/dev/null; sleep 2
# confirm checkmark (canvas 1080 → window ~680,1241)
bcmd "click 680 1241" >/dev/null; sleep 3

# durum döngüsü: event/tutorial/rewards → map olana kadar ilerle
for i in $(seq 1 15); do
  S=$(curl -s -m 6 "http://127.0.0.1:15526/api/v1/singleplayer?format=json" 2>/dev/null | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('state_type'),'|',d.get('menu_screen') or '')" 2>/dev/null)
  echo "  [$i] $S"
  case "$S" in
    map*) echo ">> MAP'e ulaşıldı"; exit 0 ;;
    *tutorial_prompt*) bash "$M" sel no >/dev/null; sleep 1; bcmd "click 680 1241" >/dev/null; sleep 2 ;;
    *character_select*) bash "$M" sel IRONCLAD >/dev/null; sleep 1; bash "$M" sel embark >/dev/null; sleep 1; bcmd "click 680 1241" >/dev/null; sleep 2 ;;
    event*) bash "$M" event 0 >/dev/null; sleep 2 ;;
    rewards*) bash "$M" proceed >/dev/null; sleep 2 ;;
    menu*popup*) bash "$M" sel yes >/dev/null; sleep 1 ;;
    *CONNECT_FAIL*|*None*) sleep 2 ;;
    *) sleep 2 ;;
  esac
done
echo ">> MAP'e ulaşılamadı (son: $S)"; exit 1
