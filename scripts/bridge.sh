#!/usr/bin/env bash
# DevBridge'e komut gönder ve sonucu bekle. Kullanım: bridge.sh "shot menu"
BRIDGE="$HOME/sts2-portrait/proton-prefix/pfx/drive_c/users/steamuser/AppData/Roaming/SlayTheSpire2/bridge"
echo "$*" > "$BRIDGE/cmd"
# 'done' güncellenene kadar bekle (maks 8sn)
for i in $(seq 1 80); do
  [ -f "$BRIDGE/cmd" ] || { sleep 0.1; break; }
  sleep 0.1
done
sleep 0.3
cat "$BRIDGE/done" 2>/dev/null
