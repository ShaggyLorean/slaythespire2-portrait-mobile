#!/usr/bin/env bash
# Ortak yardımcılar. Diğer scriptler `source` eder.
REPO="${REPO:-$HOME/sts2-portrait}"
_D="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"; [ -f "$_D/local.env" ] && . "$_D/local.env"
GAME="${GAME:-}"; [ -n "$GAME" ] || echo "HATA: GAME tanımsız — scripts/local.env oluşturun (bkz. README)" >&2
SCRATCH="${SCRATCH:-$HOME/sts2-portrait/shots}"
PREFIX="$HOME/sts2-portrait/proton-prefix"
GLOG="$PREFIX/pfx/drive_c/users/steamuser/AppData/Roaming/SlayTheSpire2/logs/godot.log"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

sts2_build() {
  dotnet build "$REPO/src/Sts2Portrait/Sts2Portrait.csproj" -c Release -v q -p:GameDir="$GAME" 2>&1 | grep -iE "error|Build succ" | head
}

sts2_install() {
  local d="$GAME/mods/Sts2Portrait"
  mkdir -p "$d"
  cp "$REPO/src/Sts2Portrait/bin/Release/net9.0/Sts2Portrait.dll" "$d/"
  cp "$REPO/src/Sts2Portrait/manifest/Sts2Portrait.json" "$d/"
}

sts2_kill() {
  # Oyunu + proton wrapper + crashpad'i öldür. Çift-instance'ı önlemek için proton
  # process GROUP'unu öldürüp HEM game HEM proton'un öldüğünü doğrula.
  # DİKKAT: 'wineserver -k' KULLANMA — tekrarlı çağrı prefix'i bozuyor (çökme cascade'i).
  local pg
  for pid in $(pgrep -f "GE-Proton11-1/proton run" 2>/dev/null); do
    pg=$(ps -o pgid= -p "$pid" 2>/dev/null | tr -d ' ')
    [ -n "$pg" ] && kill -9 -"$pg" 2>/dev/null
  done
  pkill -9 -f "SlayTheSpire2.exe" 2>/dev/null || true
  pkill -9 -f "crashpad_handler.exe" 2>/dev/null || true
  pkill -9 -f "GE-Proton11-1/proton run" 2>/dev/null || true
  # HEM game HEM proton gerçekten ölene kadar bekle (comm-tabanlı, self-match yok)
  for i in $(seq 1 40); do
    local g p
    g=$(ps -eo comm 2>/dev/null | grep -c '^SlayTheSpire2')
    p=$(ps -eo args 2>/dev/null | grep 'proton run' | grep -v grep | grep -c 'SlayThe')
    [ "$g" = "0" ] && [ "$p" = "0" ] && break
    sleep 0.5
  done
  sleep 1
}

# Modun yüklediği build mvid'ini logdan oku (freshness doğrulama)
sts2_loaded_build() { grep -oE "build=[0-9a-f-]+" "$GLOG" 2>/dev/null | tail -1; }

BRIDGE_DIR="$PREFIX/pfx/drive_c/users/steamuser/AppData/Roaming/SlayTheSpire2/bridge"

# Bridge'e komut gönder, done güncellenene kadar bekle, sonucu yaz
bcmd() {
  echo "$*" > "$BRIDGE_DIR/cmd"
  for i in $(seq 1 60); do [ -f "$BRIDGE_DIR/cmd" ] || break; sleep 0.15; done
  sleep 0.3
  cat "$BRIDGE_DIR/done" 2>/dev/null
}

# shot al + küçük önizleme üret: bshot <ad>
bshot() {
  echo "shot $1" > "$BRIDGE_DIR/cmd"
  for i in $(seq 1 60); do [ -f "$BRIDGE_DIR/cmd" ] || break; sleep 0.15; done
  sleep 0.4
  magick "$BRIDGE_DIR/$1.png" -resize 380x "$SCRATCH/$1-s.png" 2>/dev/null && echo "$SCRATCH/$1-s.png"
}

sts2_launch() {  # $1: log adı (varsayılan proton.log)
  cd "$HOME"
  setsid "$REPO/scripts/run-proton.sh" --rendering-driver vulkan > "$SCRATCH/${1:-proton.log}" 2>&1 &
  disown
}

sts2_wid() {
  local w
  w=$(DISPLAY=:0 xdotool search --name "Slay the Spire 2" 2>/dev/null | head -1)
  [ -z "$w" ] && w=$(DISPLAY=:0 xdotool search --class "SlayTheSpire2" 2>/dev/null | head -1)
  echo "$w"
}

# Menü yüklenene kadar bekle
sts2_wait_menu() {
  until grep -q "main menu loaded (complete)" "$GLOG" 2>/dev/null; do
    pgrep -f "SlayTheSpire2.exe" >/dev/null || { echo "PROC DIED"; return 1; }
    sleep 3
  done
}

# Pencereyi odakla + ekran görüntüsü al: $1=çıktı adı
sts2_shot() {
  local wid; wid=$(sts2_wid)
  [ -z "$wid" ] && { echo "NO WINDOW"; return 1; }
  DISPLAY=:0 xdotool windowactivate "$wid" >/dev/null 2>&1
  sleep 1.5
  DISPLAY=:0 import -window "$wid" "$SCRATCH/$1" 2>/dev/null && echo "shot -> $SCRATCH/$1"
}
