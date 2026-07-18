#!/usr/bin/env bash
# MCP kısayolları. Kullanım: mcp.sh state | mcp.sh sel <option> | mcp.sh post '<json>'
source "$HOME/sts2-portrait/scripts/lib.sh"
PORT=15526
BASE="http://127.0.0.1:$PORT/api/v1/singleplayer"

case "$1" in
  state)
    curl -s -m 4 "$BASE?format=markdown" 2>/dev/null | python3 -c "import sys,json;print(json.load(sys.stdin).get('result',''))" 2>/dev/null \
      || curl -s -m 4 "$BASE?format=json" 2>/dev/null | python3 -c "import sys,json;d=json.load(sys.stdin);print('state:',d.get('state_type'),'| menu:',d.get('menu_screen'),'| opts:',[o.get('id') for o in d.get('options',[])])" 2>/dev/null
    ;;
  st)  # kısa state
    curl -s -m 4 "$BASE?format=json" 2>/dev/null | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('state_type'),'|',d.get('menu_screen') or '',[o.get('id') for o in d.get('options',[])][:8])" 2>/dev/null || echo "CONNECT_FAIL"
    ;;
  sel)  # menu_select
    curl -s -m 4 -X POST "$BASE" -H "Content-Type: application/json" -d "{\"action\":\"menu_select\",\"option\":\"$2\"}" 2>/dev/null | python3 -c "import sys,json;print(json.load(sys.stdin).get('message',json.load(sys.stdin)))" 2>/dev/null
    ;;
  alive)
    pgrep -f "SlayTheSpire2.exe --rendering" >/dev/null && echo ALIVE || echo DEAD
    ;;
  *) echo "usage: mcp.sh state|st|sel <opt>|alive" ;;
esac
