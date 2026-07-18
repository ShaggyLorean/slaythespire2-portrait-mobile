#!/usr/bin/env bash
# MCP kısayolları (curl tabanlı, otonom çalışma için).
# Kullanım:
#   mcp.sh st                          → kısa state
#   mcp.sh state                       → markdown state
#   mcp.sh sel <option>                → menu_select (singleplayer, IRONCLAD, embark, no, yes...)
#   mcp.sh act '<json>'                → ham POST body
#   mcp.sh play <card_index> [target]  → play_card
#   mcp.sh endturn                     → end_turn
#   mcp.sh map <node_index>            → choose_map_node
#   mcp.sh event <option_index>        → choose_event_option
#   mcp.sh reward <index>              → claim_reward
#   mcp.sh pickcard <index>            → select_card_reward
#   mcp.sh skipcard                    → skip_card_reward
#   mcp.sh proceed                     → proceed (to map)
#   mcp.sh rest <index>                → choose_rest_option
#   mcp.sh shop <index>                → shop_purchase
#   mcp.sh relic <index> | skiprelic   → relic seçimi
#   mcp.sh alive
source "$HOME/sts2-portrait/scripts/lib.sh"
BASE="http://127.0.0.1:15526/api/v1/singleplayer"

post() { curl -s -m 6 -X POST "$BASE" -H "Content-Type: application/json" -d "$1" 2>/dev/null | python3 -c "import sys,json
try:
  d=json.load(sys.stdin); print(d.get('message') or d.get('error') or json.dumps(d)[:200])
except: print('POST_FAIL')" 2>/dev/null; }

case "$1" in
  state) curl -s -m 6 "$BASE?format=markdown" 2>/dev/null | python3 -c "import sys,json;print(json.load(sys.stdin).get('result',''))" 2>/dev/null || echo CONNECT_FAIL ;;
  st)  curl -s -m 6 "$BASE?format=json" 2>/dev/null | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('state_type'),'|',d.get('menu_screen') or '',[o.get('id') for o in d.get('options',[])][:8])" 2>/dev/null || echo CONNECT_FAIL ;;
  sel) post "{\"action\":\"menu_select\",\"option\":\"$2\"}" ;;
  act) post "$2" ;;
  play) if [ -n "$3" ]; then post "{\"action\":\"play_card\",\"card_index\":$2,\"target\":\"$3\"}"; else post "{\"action\":\"play_card\",\"card_index\":$2}"; fi ;;
  endturn) post '{"action":"end_turn"}' ;;
  map) post "{\"action\":\"choose_map_node\",\"node_index\":$2}" ;;
  event) post "{\"action\":\"choose_event_option\",\"option_index\":$2}" ;;
  reward) post "{\"action\":\"claim_reward\",\"reward_index\":$2}" ;;
  pickcard) post "{\"action\":\"select_card_reward\",\"card_index\":$2}" ;;
  skipcard) post '{"action":"skip_card_reward"}' ;;
  proceed) post '{"action":"proceed"}' ;;
  rest) post "{\"action\":\"choose_rest_option\",\"option_index\":$2}" ;;
  shop) post "{\"action\":\"shop_purchase\",\"item_index\":$2}" ;;
  relic) post "{\"action\":\"select_relic\",\"relic_index\":$2}" ;;
  skiprelic) post '{"action":"skip_relic_selection"}' ;;
  alive) pgrep -f "SlayTheSpire2.exe --rendering" >/dev/null && echo ALIVE || echo DEAD ;;
  *) echo "bkz. script başı" ;;
esac
