#!/usr/bin/env bash
# Tam PC geliştirme döngüsü: derle → kur → oyunu Proton'la başlat → menüde ekran görüntüsü.
# Kullanım: dev-cycle.sh [bekleme_saniye]
set -uo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
_D="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"; [ -f "$_D/local.env" ] && . "$_D/local.env"
GAME="${GAME:-}"; [ -n "$GAME" ] || echo "HATA: GAME tanımsız — scripts/local.env oluşturun (bkz. README)" >&2
SCRATCH="${SCRATCH:-/tmp/claude-1000/-home-whispersgone/d17e17a8-955d-4d3d-a8a1-06a439a9f294/scratchpad}"
PREFIX="$HOME/sts2-portrait/proton-prefix"
GLOG="$PREFIX/pfx/drive_c/users/steamuser/AppData/Roaming/SlayTheSpire2/logs/godot.log"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

echo ">> build"
dotnet build "$REPO/src/Sts2Portrait/Sts2Portrait.csproj" -c Release -v q || exit 1

echo ">> install"
MOD_DIR="$GAME/mods/Sts2Portrait"
mkdir -p "$MOD_DIR"
cp "$REPO/src/Sts2Portrait/bin/Release/net9.0/Sts2Portrait.dll" "$MOD_DIR/"
cp "$REPO/src/Sts2Portrait/manifest/Sts2Portrait.json" "$MOD_DIR/"

echo ">> kill old"
pkill -9 -f "SlayTheSpire2.exe" 2>/dev/null
sleep 2

echo ">> launch"
cd "$HOME"
setsid "$REPO/scripts/run-proton.sh" --rendering-driver vulkan > "$SCRATCH/proton-cycle.log" 2>&1 &
echo "launched"
